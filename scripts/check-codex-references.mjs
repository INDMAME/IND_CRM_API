import { existsSync, readFileSync, readdirSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const DEFAULT_SKILL = "ind-crm-backend-guardrails";

const scriptDir = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDir, "..");
const codexRoot = join(repositoryRoot, ".codex");
const skillName = process.argv[2] || DEFAULT_SKILL;
const referencesDir = join(codexRoot, "skills", skillName, "references");

/**
 * Return the most direct npm command to synchronize one skill.
 */
function getSyncCommand(skill) {
  if (skill === DEFAULT_SKILL) {
    return "npm run sync:local:skill:references";
  }

  return `npm run sync:skill:references -- ${skill}`;
}

/**
 * Return sorted top-level markdown files from a directory.
 */
function getTopLevelMarkdownFiles(dirPath) {
  return readdirSync(dirPath, { withFileTypes: true })
    .filter((entry) => entry.isFile() && entry.name.toLowerCase().endsWith(".md"))
    .map((entry) => entry.name)
    .sort((a, b) => a.localeCompare(b));
}

/**
 * Compare .codex top-level markdown files and local skill references.
 */
function checkSync() {
  if (!existsSync(referencesDir)) {
    console.error(`[error] References directory does not exist: ${referencesDir}`);
    console.error(`Run: ${getSyncCommand(skillName)}`);
    process.exitCode = 1;
    return;
  }

  const sourceFiles = getTopLevelMarkdownFiles(codexRoot);
  const referenceFiles = getTopLevelMarkdownFiles(referencesDir);

  const sourceSet = new Set(sourceFiles);
  const referenceSet = new Set(referenceFiles);

  const missingInReferences = sourceFiles.filter((fileName) => !referenceSet.has(fileName));
  const staleInReferences = referenceFiles.filter((fileName) => !sourceSet.has(fileName));
  const contentMismatches = [];

  for (const fileName of sourceFiles) {
    if (!referenceSet.has(fileName)) {
      continue;
    }

    const sourcePath = join(codexRoot, fileName);
    const referencePath = join(referencesDir, fileName);
    const sourceContent = readFileSync(sourcePath, "utf8");
    const referenceContent = readFileSync(referencePath, "utf8");

    if (sourceContent !== referenceContent) {
      contentMismatches.push(fileName);
    }
  }

  const hasDrift =
    missingInReferences.length > 0 ||
    staleInReferences.length > 0 ||
    contentMismatches.length > 0;

  if (!hasDrift) {
    console.log(`[ok] .codex references are synchronized for ${skillName}.`);
    return;
  }

  console.error(`[error] .codex references are out of sync for ${skillName}.`);

  if (missingInReferences.length > 0) {
    console.error("- Missing in references:", missingInReferences.join(", "));
  }

  if (staleInReferences.length > 0) {
    console.error("- Stale in references:", staleInReferences.join(", "));
  }

  if (contentMismatches.length > 0) {
    console.error("- Content mismatch:", contentMismatches.join(", "));
  }

  console.error(`Run: ${getSyncCommand(skillName)}`);
  process.exitCode = 1;
}

checkSync();
