import { spawnSync } from "node:child_process";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const LOCAL_SKILL = "ind-crm-backend-guardrails";

const scriptDir = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDir, "..");
const syncScriptPath = join(scriptDir, "sync-skill-references.mjs");
const referencesPath = join(".codex", "skills", LOCAL_SKILL, "references");

/**
 * Run one command and stop the commit flow if it fails.
 */
function runCommandOrExit(command, args) {
  const result = spawnSync(command, args, {
    cwd: repositoryRoot,
    stdio: "inherit",
  });

  if (result.error) {
    console.error(`[error] Failed to run command: ${command}`);
    console.error(result.error.message);
    process.exit(1);
  }

  if (typeof result.status === "number" && result.status !== 0) {
    process.exit(result.status);
  }
}

console.log(`[pre-commit] Syncing references for ${LOCAL_SKILL}.`);
runCommandOrExit(process.execPath, [syncScriptPath, LOCAL_SKILL]);

console.log(`[pre-commit] Staging synchronized references from ${referencesPath}.`);
runCommandOrExit("git", ["add", referencesPath]);

console.log("[pre-commit] Local skill references are synchronized.");
