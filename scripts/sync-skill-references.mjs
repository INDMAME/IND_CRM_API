import { copyFileSync, mkdirSync, readdirSync, rmSync, watch } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const WATCH_FLAG = "--watch";
const DEFAULT_LOCAL_SKILL = "ind-crm-backend-guardrails";
const DEBOUNCE_MS = 200;

const scriptDir = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDir, "..");
const codexRoot = join(repositoryRoot, ".codex");

/**
 * Validate skill name input to keep operations inside .codex/skills/<name>.
 */
function getSkillNameOrExit(args) {
  const skillName = args.find((arg) => arg !== WATCH_FLAG) || DEFAULT_LOCAL_SKILL;

  if (
    skillName.includes("/") ||
    skillName.includes("\\") ||
    skillName.includes("..") ||
    skillName.trim().length === 0
  ) {
    console.error(`[error] Invalid skill name: ${skillName}`);
    process.exit(1);
  }

  return skillName;
}

/**
 * Return top-level markdown files from .codex as source of truth.
 */
function getSourceMarkdownFiles() {
  return readdirSync(codexRoot, { withFileTypes: true })
    .filter((entry) => entry.isFile() && entry.name.toLowerCase().endsWith(".md"))
    .map((entry) => entry.name)
    .sort((a, b) => a.localeCompare(b));
}

/**
 * Synchronize markdown references for one local skill.
 */
function syncOnce(referencesDir) {
  mkdirSync(referencesDir, { recursive: true });

  const sourceFiles = getSourceMarkdownFiles();
  const sourceSet = new Set(sourceFiles);

  let copiedCount = 0;
  for (const fileName of sourceFiles) {
    const sourcePath = join(codexRoot, fileName);
    const destinationPath = join(referencesDir, fileName);
    copyFileSync(sourcePath, destinationPath);
    copiedCount += 1;
    console.log(`[sync] ${fileName}`);
  }

  let removedCount = 0;
  const destinationEntries = readdirSync(referencesDir, { withFileTypes: true });
  for (const entry of destinationEntries) {
    if (!entry.isFile() || !entry.name.toLowerCase().endsWith(".md")) {
      continue;
    }

    if (!sourceSet.has(entry.name)) {
      rmSync(join(referencesDir, entry.name), { force: true });
      removedCount += 1;
      console.log(`[remove] ${entry.name}`);
    }
  }

  console.log(
    `[done] Synced ${copiedCount} markdown file(s) and removed ${removedCount} stale file(s).`,
  );
}

/**
 * Keep references synchronized whenever .codex top-level markdown files are edited.
 */
function watchAndSync(referencesDir) {
  syncOnce(referencesDir);
  console.log("[watch] Listening for changes in .codex/*.md");

  let syncTimer = null;
  const scheduleSync = () => {
    if (syncTimer) {
      clearTimeout(syncTimer);
    }

    syncTimer = setTimeout(() => {
      syncOnce(referencesDir);
      syncTimer = null;
    }, DEBOUNCE_MS);
  };

  watch(codexRoot, { persistent: true }, (_eventType, fileName) => {
    if (!fileName || !fileName.toLowerCase().endsWith(".md")) {
      return;
    }

    // Ignore nested paths; only top-level .codex/*.md are synchronized.
    if (fileName.includes("/") || fileName.includes("\\")) {
      return;
    }

    scheduleSync();
  });
}

const args = process.argv.slice(2);
const watchMode = args.includes(WATCH_FLAG);
const skillName = getSkillNameOrExit(args);
const referencesDir = join(codexRoot, "skills", skillName, "references");

console.log(`[info] Skill: ${skillName}`);
console.log(`[info] Default local skill: ${DEFAULT_LOCAL_SKILL}`);
console.log(`[info] Source: ${codexRoot}`);
console.log(`[info] Destination: ${referencesDir}`);

if (watchMode) {
  watchAndSync(referencesDir);
} else {
  syncOnce(referencesDir);
}
