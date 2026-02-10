import { chmodSync, existsSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDir, "..");
const hooksPath = ".githooks";
const preCommitHookPath = join(repositoryRoot, hooksPath, "pre-commit");

/**
 * Configure Git hook path and stop if Git returns an error.
 */
function runGitOrExit(args) {
  const result = spawnSync("git", args, {
    cwd: repositoryRoot,
    stdio: "inherit",
  });

  if (result.error) {
    console.error("[error] Git is required to configure hooks.");
    console.error(result.error.message);
    process.exit(1);
  }

  if (typeof result.status === "number" && result.status !== 0) {
    process.exit(result.status);
  }
}

if (!existsSync(preCommitHookPath)) {
  console.error(`[error] Missing hook file: ${preCommitHookPath}`);
  process.exit(1);
}

runGitOrExit(["config", "core.hooksPath", hooksPath]);

try {
  chmodSync(preCommitHookPath, 0o755);
} catch {
  // Ignore chmod errors on filesystems that do not track executable flags.
}

console.log(`[done] Git hooks path configured to ${hooksPath}.`);
