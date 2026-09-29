using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Newtonsoft.Json;

namespace IND_CRM_API.Services
{
    // Keeps cleanup progress outside the deployment directory and serializes work for each sheet.
    public sealed class ExpenseSheetDeletionService
    {
        private const long MaximumJournalBytes = 16 * 1024 * 1024;
        private readonly string _directory;

        // A custom directory is intended for isolated tests or an explicitly configured local journal.
        public ExpenseSheetDeletionService(string directory = null)
        {
            _directory = Path.GetFullPath(directory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "IND_CRM_API", "expense-sheet-deletions"));
        }

        // Persists intent before deletion and resumes idempotent cleanup using the original inventory.
        public DeletionProgress Execute(string scope, string actor, string owner,
            Func<DeletionSnapshot> readSheet, Action deleteSheet,
            Action<DeletionTicket> deleteTicket, Action<string> deleteBlob)
        {
            ValidateIdentity(scope, actor, owner);
            if (readSheet == null) throw new ArgumentNullException(nameof(readSheet));
            if (deleteSheet == null) throw new ArgumentNullException(nameof(deleteSheet));
            if (deleteTicket == null) throw new ArgumentNullException(nameof(deleteTicket));
            if (deleteBlob == null) throw new ArgumentNullException(nameof(deleteBlob));
            EnsureDirectory();
            var key = Hash(scope);
            using (AcquireLock(key))
            {
                var file = Path.Combine(_directory, key + ".json");
                var progress = Load(file, scope, actor, owner) ?? new DeletionProgress
                {
                    Scope = scope, Actor = actor, Owner = owner
                };
                if (progress.Completed) return progress;

                if (!progress.SheetDeleted)
                {
                    // The adapter revalidates ownership and builds a current inventory before every attempt.
                    var snapshot = readSheet();
                    if (snapshot == null)
                    {
                        if (!progress.DeleteAttempted)
                            throw new InvalidOperationException("Hoja de gastos no encontrada.");
                        // A previous delete may have committed even when its response was lost.
                        progress.SheetDeleted = true;
                        Save(file, progress);
                    }
                    else
                    {
                        ApplySnapshot(progress, snapshot);
                        progress.DeleteAttempted = true;
                        Save(file, progress);
                        try
                        {
                            deleteSheet();
                        }
                        catch (DeletionRejectedException)
                        {
                            // A confirmed rejection cannot justify cleanup after a later unrelated deletion.
                            progress.DeleteAttempted = false;
                            Save(file, progress);
                            throw;
                        }
                        progress.SheetDeleted = true;
                        Save(file, progress);
                    }
                }

                foreach (var ticket in progress.Tickets)
                {
                    if (!ticket.Deleted)
                    {
                        // The adapter must accept an already absent ticket after an ambiguous prior response.
                        deleteTicket(new DeletionTicket { FileId = ticket.FileId, BlobUrl = ticket.BlobUrl });
                        ticket.Deleted = true;
                        Save(file, progress);
                    }
                    if (!ticket.BlobDeleted)
                    {
                        if (!string.IsNullOrWhiteSpace(ticket.BlobUrl)) deleteBlob(ticket.BlobUrl);
                        ticket.BlobDeleted = true;
                        Save(file, progress);
                    }
                }
                return progress;
            }
        }

        // Returns only a complete journal snapshot and refuses another actor or owner.
        public DeletionProgress Read(string scope, string actor, string owner)
        {
            ValidateIdentity(scope, actor, owner);
            EnsureDirectory();
            var key = Hash(scope);
            using (AcquireLock(key))
                return Load(Path.Combine(_directory, key + ".json"), scope, actor, owner);
        }

        // Uses a nonshared file handle so concurrent requests or API processes cannot enter together.
        private FileStream AcquireLock(string key)
        {
            var file = Path.Combine(_directory, key + ".lock");
            RejectReparsePoints(file);
            return new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        // Rejects incomplete or contradictory records instead of treating them as a new operation.
        private static DeletionProgress Load(string file, string scope, string actor, string owner)
        {
            RejectReparsePoints(file);
            if (!File.Exists(file)) return null;
            DeletionProgress value;
            try
            {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length <= 0 || stream.Length > MaximumJournalBytes)
                        throw new InvalidOperationException("El registro de borrado no tiene un tamano valido.");
                    using (var reader = new StreamReader(stream, new UTF8Encoding(false, true)))
                        value = JsonConvert.DeserializeObject<DeletionProgress>(reader.ReadToEnd(),
                            new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is DecoderFallbackException)
            {
                throw new InvalidOperationException("El registro de borrado no es valido.", ex);
            }
            if (value == null || value.Version != 1 || value.Tickets == null ||
                !string.Equals(value.Scope, scope, StringComparison.Ordinal) ||
                value.PreservedManualTicketCount < 0 ||
                (!value.MetadataAvailable && value.Tickets.Count != 0) ||
                (value.SheetDeleted && !value.DeleteAttempted))
                throw new InvalidOperationException("El registro de borrado no es valido.");
            if (!string.Equals(value.Actor, actor, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(value.Owner, owner, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("La operacion de borrado pertenece a otro usuario o propietario.");
            ValidateTickets(value.Tickets);
            if (value.Tickets.Any(ticket => (ticket.Deleted && !value.SheetDeleted) ||
                (ticket.BlobDeleted && !ticket.Deleted)))
                throw new InvalidOperationException("El progreso de borrado no es coherente.");
            return value;
        }

        // Copies only a validated fresh inventory, preserving legacy metadata and manual association counts.
        private static void ApplySnapshot(DeletionProgress progress, DeletionSnapshot snapshot)
        {
            if (snapshot.Tickets == null || snapshot.PreservedManualTicketCount < 0 ||
                (!snapshot.MetadataAvailable && snapshot.Tickets.Count != 0))
                throw new InvalidOperationException("El inventario de borrado no es valido.");
            ValidateTickets(snapshot.Tickets);
            if (snapshot.Tickets.Any(ticket => ticket.Deleted || ticket.BlobDeleted))
                throw new InvalidOperationException("El inventario contiene progreso de otra operacion.");
            progress.MetadataAvailable = snapshot.MetadataAvailable;
            progress.PreservedManualTicketCount = snapshot.PreservedManualTicketCount;
            progress.Tickets = snapshot.Tickets.Select(ticket => new DeletionTicket
            {
                FileId = ticket.FileId.Trim(), BlobUrl = (ticket.BlobUrl ?? string.Empty).Trim()
            }).ToList();
        }

        // Rejects ambiguous ticket identities before calling any destructive adapter.
        private static void ValidateTickets(IReadOnlyCollection<DeletionTicket> tickets)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ticket in tickets)
            {
                if (ticket == null || string.IsNullOrWhiteSpace(ticket.FileId) ||
                    ticket.FileId.Length > 256 || !ids.Add(ticket.FileId.Trim()))
                    throw new InvalidOperationException("El inventario contiene identificadores de ticket no validos o repetidos.");
            }
        }

        // Creates a private journal directory before writing business identifiers.
        private void EnsureDirectory()
        {
            RejectReparsePoints(_directory);
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            var identities = new[]
            {
                WindowsIdentity.GetCurrent().User,
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
            };
            foreach (var identity in identities.Distinct())
                security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
            Directory.CreateDirectory(_directory, security);
            RejectReparsePoints(_directory);
            new DirectoryInfo(_directory).SetAccessControl(security);
        }

        // Rejects junctions and symbolic links in the journal path and all existing ancestors.
        private static void RejectReparsePoints(string path)
        {
            for (var current = Path.GetFullPath(path); !string.IsNullOrWhiteSpace(current);
                current = Path.GetDirectoryName(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("El registro de borrado no admite rutas redirigidas.");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }

        // Flushes a new file before atomically replacing the last committed journal.
        private static void Save(string file, DeletionProgress progress)
        {
            RejectReparsePoints(file);
            var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(progress));
                if (bytes.Length > MaximumJournalBytes)
                    throw new InvalidOperationException("El inventario de borrado es demasiado grande.");
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                RejectReparsePoints(file);
                if (File.Exists(file)) File.Replace(temporary, file, null);
                else File.Move(temporary, file);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        // Requires stable server-derived identifiers before constructing the journal key.
        private static void ValidateIdentity(string scope, string actor, string owner)
        {
            if (string.IsNullOrWhiteSpace(scope) || scope.Length > 4096)
                throw new ArgumentException("El ambito de borrado no es valido.", nameof(scope));
            if (string.IsNullOrWhiteSpace(actor) || actor.Length > 1024)
                throw new ArgumentException("El actor de borrado no es valido.", nameof(actor));
            if (string.IsNullOrWhiteSpace(owner) || owner.Length > 256)
                throw new ArgumentException("El propietario de borrado no es valido.", nameof(owner));
        }

        // Keeps tenant, environment, company and sheet identifiers out of filesystem names.
        private static string Hash(string scope)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(scope))).Replace("-", "");
        }
    }

    // Contains ticket-origin assets only; the adapter excludes manual associations.
    public sealed class DeletionSnapshot
    {
        public List<DeletionTicket> Tickets { get; set; } = new List<DeletionTicket>();
        public bool MetadataAvailable { get; set; } = true;
        public int PreservedManualTicketCount { get; set; }
    }

    // Records every completed irreversible step for one authenticated deletion.
    public sealed class DeletionProgress
    {
        [JsonProperty(Required = Required.Always)] public int Version { get; set; } = 1;
        [JsonProperty(Required = Required.Always)] public string Scope { get; set; }
        [JsonProperty(Required = Required.Always)] public string Actor { get; set; }
        [JsonProperty(Required = Required.Always)] public string Owner { get; set; }
        [JsonProperty(Required = Required.Always)] public bool DeleteAttempted { get; set; }
        [JsonProperty(Required = Required.Always)] public bool SheetDeleted { get; set; }
        [JsonProperty(Required = Required.Always)] public bool MetadataAvailable { get; set; } = true;
        [JsonProperty(Required = Required.Always)] public int PreservedManualTicketCount { get; set; }
        [JsonProperty(Required = Required.Always)] public List<DeletionTicket> Tickets { get; set; } = new List<DeletionTicket>();
        [JsonIgnore] public bool Completed => SheetDeleted && Tickets.All(ticket => ticket.Deleted && ticket.BlobDeleted);
    }

    // Saves the original blob address before AX removes the ticket record.
    public sealed class DeletionTicket
    {
        [JsonProperty(Required = Required.Always)] public string FileId { get; set; }
        [JsonProperty(Required = Required.AllowNull)] public string BlobUrl { get; set; }
        [JsonProperty(Required = Required.Always)] public bool Deleted { get; set; }
        [JsonProperty(Required = Required.Always)] public bool BlobDeleted { get; set; }
    }

    // Distinguishes an authoritative business rejection from a lost or ambiguous delete response.
    public sealed class DeletionRejectedException : Exception
    {
        public DeletionRejectedException(Exception innerException)
            : base("La eliminacion de la hoja fue rechazada sin confirmar cambios.", innerException)
        {
        }
    }
}
