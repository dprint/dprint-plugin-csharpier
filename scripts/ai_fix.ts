/**
 * Fixes this plugin after a CSharpier update broke it using OpenAI Codex, in two
 * stages that each run Codex with a different model:
 *
 *   1. a Codex agentic session edits the source and runs the checks until they
 *      pass, then
 *   2. a SEPARATE reviewer model reviews the result with Codex, so it can
 *      investigate (read the CSharpier source, grep the repo). It is instructed
 *      to review only and not modify anything. If it finds blocking issues
 *      they are fed back to the stage-1 Codex for another pass, bounded by
 *      `REVIEW_MAX_ROUNDS`. If it still isn't approved, this throws so the
 *      workflow fails and nothing is published.
 *
 * This is only called when the checks fail.
 *
 * Codex must NOT commit or push -- `update.ts` captures the working tree
 * changes into the CSharpier update commit afterwards.
 */
import $ from "jsr:@david/dax@^0.47.0";

export interface AiFixOptions {
  /** CSharpier version before the update. */
  fromVersion: string;
  /** CSharpier version being updated to. */
  toVersion: string;
  /** Combined output of the failing checks. */
  checkOutput: string;
  /** Commands of the checks that must pass. */
  checkCommands: string[];
}

/** Max number of times the reviewer may send changes back to Codex. */
const REVIEW_MAX_ROUNDS = 2;

export async function aiFixCSharpierUpdate(options: AiFixOptions): Promise<void> {
  const apiKey = requireApiKey();
  await ensureCodexInstalled();
  await codexLogin(apiKey);

  // stage 1: let Codex fix the update.
  await runCodex(buildFixPrompt(options));

  // stage 2: independent second-model review, with a bounded refix loop.
  for (let round = 1;; round++) {
    const review = await reviewChanges(options);
    logReview(review);
    if (review.approved) {
      return;
    }
    if (round > REVIEW_MAX_ROUNDS) {
      const blocking = review.issues.filter((i) => i.severity === "blocking");
      throw new Error(
        `AI reviewer did not approve after ${REVIEW_MAX_ROUNDS} refix round(s). Blocking issues:\n`
          + blocking.map((i) => `  - ${i.description}`).join("\n"),
      );
    }
    $.logStep(`Reviewer requested changes — Codex refix round ${round}...`);
    await runCodex(buildRefixPrompt(review, options));
  }
}

// stage 1: Codex ---------------------------------------------------------------

async function runCodex(prompt: string): Promise<void> {
  const args = ["exec", "--dangerously-bypass-approvals-and-sandbox", "--skip-git-repo-check"];
  const model = Deno.env.get("CODEX_MODEL");
  if (model) {
    args.push("--model", model);
  }
  args.push(prompt);

  $.logStep("Running Codex...");
  await $`codex ${args}`;
}

function buildFixPrompt(options: AiFixOptions): string {
  const { fromVersion, toVersion, checkOutput } = options;
  return [
    `You are updating "dprint-plugin-csharpier", a dprint plugin written in C# that wraps CSharpier (an opinionated C# and XML code formatter) to format C# and XML code. It is compiled ahead of time to a Wasm module with NativeAOT-LLVM.`,
    ``,
    `The CSharpier.Core package was upgraded from ${fromVersion} to ${toVersion} and the checks now fail. This is almost always because the plugin calls CSharpier's INTERNAL API (the same one its CLI uses), which changes without notice, because CSharpier intentionally formats some code differently, or because the upgrade uses something that doesn't work after trimming and ahead of time compilation to Wasm (ex. reflection, threads, timers, or WASI preview 2 imports that dprint doesn't provide).`,
    ``,
    `The checks currently fail with the output below. Use it as your starting point instead of re-running the checks just to rediscover the errors:`,
    "```",
    truncateHead(checkOutput.trim()),
    "```",
    ``,
    `Your goal: make the checks pass while formatting the same way as the CSharpier ${toVersion} CLI. Do NOT commit or push; only edit files in the working tree.`,
    ``,
    describeWiring(),
    ``,
    `To see what changed in CSharpier, read its source at https://github.com/belav/csharpier (clone the ${toVersion} tag into a temporary directory OUTSIDE this repository if that helps) and its \`CHANGELOG.md\`. The code the plugin calls is under \`Src/CSharpier.Core\` (ex. \`CodeFormatter.cs\`, \`PrinterOptions.cs\`, \`CodeFormatterResult.cs\`, \`GeneratedCodeUtilities.cs\`) and the CLI's usage of it is in \`Src/CSharpier.Cli/CommandLineFormatter.cs\`. The compiled package is in \`~/.nuget/packages/csharpier.core\`.`,
    ``,
    `Rules:`,
    `1. Keep CSharpier.Core at ${toVersion}. Do not downgrade or pin it to make the checks pass.`,
    `2. Do NOT delete, skip, or weaken tests to make them pass. Only change a test's expected output when CSharpier itself intentionally formats that code differently in ${toVersion}, and say so in your final message.`,
    `3. Never edit \`deployment/schema.json\` by hand. It is generated and is rewritten by running \`dotnet test\` with the \`UPDATE=1\` environment variable.`,
    `4. Do NOT edit \`README.md\`.`,
    `5. Preserve the existing code style. Public members go above private ones. New comments start lowercase unless multiple sentences.`,
    `6. When done, ALL of these must pass (the Wasm build is what actually ships) — iterate until they are all clean:`,
    ...describeChecks(options),
    `7. Do not change the plugin's own version in the project file, do not run git commit, and do not push.`,
  ].join("\n");
}

function buildRefixPrompt(review: ReviewResult, options: AiFixOptions): string {
  return [
    `An independent reviewer examined your changes to dprint-plugin-csharpier and found issues that must be fixed. Address every blocking issue below, keeping all the checks green. Do not commit or push.`,
    ``,
    `Reviewer summary: ${review.summary}`,
    ``,
    `Issues:`,
    ...review.issues.map((i) => `  - [${i.severity}] ${i.description}`),
    ``,
    describeWiring(),
    ``,
    `The checks that must pass:`,
    ...describeChecks(options),
  ].join("\n");
}

function describeWiring(): string {
  return [
    `How the plugin is wired:`,
    `- \`DprintPluginCSharpier/Formatters/FileFormatter.cs\` -> calls CSharpier's internal \`CodeFormatter.FormatAsync\` with \`PrinterOptions\` (the same API its CLI uses), handles encodings and line endings around it, and turns syntax errors and failures into exceptions so a file is never overwritten with CSharpier's empty output.`,
    `- \`DprintPluginCSharpier/DprintPluginCSharpier.csproj\` -> the \`<Publicize>\` item (Krafs.Publicizer) is what makes CSharpier's internal types accessible, and the \`wasi-wasm\` property groups configure the NativeAOT-LLVM build, including feature switches that keep unsupported code out of the Wasm module.`,
    `- \`DprintPluginCSharpier/ILLink.Substitutions.xml\` -> replaces method bodies when compiling to Wasm so the module only imports the WASI preview 1 functions that dprint provides. Check the imports of the built module if dprint fails to load it.`,
    `- \`DprintPluginCSharpier/Configuration/\` -> resolves the dprint configuration into the options passed to CSharpier, using CSharpier's defaults.`,
    `- \`DprintPluginCSharpier/Plugin.cs\` -> the file extensions the plugin formats, which should match \`PrinterOptions.GetFormatter\` in CSharpier.`,
    `- \`DprintPluginCSharpier/Serialization/PluginJson.cs\` -> hand written JSON, because reflection-based serialization isn't available after ahead of time compilation.`,
    `- \`DprintPluginCSharpier/WasmExports.cs\` -> the functions dprint calls on the Wasm module.`,
    `- \`DprintPluginCSharpier.Tests/\` -> NUnit tests. The ones in the \`Wasm\` category run the built Wasm module in a real dprint executable and compare its output with the .NET runtime's.`,
  ].join("\n");
}

function describeChecks(options: AiFixOptions): string[] {
  return options.checkCommands.map((command) => `     ${command}`);
}

// keep the tail of long check output -- the errors and the build summary are
// at the end, which is the most useful part for the AI.
function truncateHead(text: string, max = 20_000): string {
  return text.length > max ? `... (truncated)\n${text.slice(text.length - max)}` : text;
}

// stage 2: independent reviewer ------------------------------------------------

interface ReviewResult {
  approved: boolean;
  summary: string;
  issues: ReviewIssue[];
}

interface ReviewIssue {
  severity: "blocking" | "nit";
  description: string;
}

// The reviewer runs Codex too so it can actually investigate (read the CSharpier
// source, grep the repo). Its verdict is captured as JSON via --output-last-message.
//
// NOTE: we deliberately do NOT use `--sandbox read-only` here. That makes Codex
// wrap commands in bubblewrap, which cannot set up its network namespace on the
// GitHub Actions runner ("bwrap: loopback: Failed RTM_NEWADDR: Operation not
// permitted"), so every command the reviewer runs fails before executing. We
// use the same no-sandbox mode as the fixer and enforce read-only via the
// prompt instead. (CI is itself a throwaway VM, and the final check gate in
// update.ts re-verifies whatever ends up in the working tree.)
async function reviewChanges(options: AiFixOptions): Promise<ReviewResult> {
  // default to a different model than the Codex fixer so the review is a
  // genuinely independent second opinion.
  const model = Deno.env.get("REVIEW_MODEL") ?? "gpt-6-astra";

  // record intent-to-add so any files Codex created also show in `git diff`.
  await $`git add -N .`.quiet();

  const outputFile = await Deno.makeTempFile({ prefix: "codex-review-", suffix: ".json" });
  try {
    $.logStep(`Reviewing changes with Codex (${model})...`);
    const args = [
      "exec",
      "--skip-git-repo-check",
      "--dangerously-bypass-approvals-and-sandbox",
      "--model",
      model,
      "--output-last-message",
      outputFile,
      buildReviewPrompt(options),
    ];
    await $`codex ${args}`;
    return parseReview(await Deno.readTextFile(outputFile));
  } finally {
    await Deno.remove(outputFile).catch(() => {});
  }
}

function buildReviewPrompt(options: AiFixOptions): string {
  return [
    `You are an independent reviewer for dprint-plugin-csharpier, a dprint plugin written in C# that wraps CSharpier to format C# and XML code and is compiled ahead of time to a Wasm module with NativeAOT-LLVM. The CSharpier.Core package was just upgraded from ${options.fromVersion} to ${options.toVersion}, which broke the plugin, and another AI edited it to fix that. Review the UNCOMMITTED working-tree changes.`,
    `IMPORTANT: this is REVIEW ONLY. Read any files and run read-only commands (git diff, cat, grep, find, etc.) to investigate, but you MUST NOT edit, create, or delete any files, and MUST NOT run git commit or git push. Leave the working tree exactly as you found it.`,
    ``,
    `Investigate as needed:`,
    `- Run \`git --no-pager diff\` (and \`git status\`) to see exactly what changed, including any new files. The package version changes in the \`.csproj\` files and a regenerated \`deployment/schema.json\` are expected.`,
    `- Verify against the REAL CSharpier ${options.toVersion} source at https://github.com/belav/csharpier (clone it into a temporary directory OUTSIDE this repository if that helps).`,
    `- Read the plugin files to confirm they are consistent with each other and with upstream.`,
    ``,
    describeWiring(),
    ``,
    `Verify specifically:`,
    `- The plugin still formats with CSharpier the same way as its ${options.toVersion} CLI. A change that makes the checks pass by bypassing or stubbing out formatting is a blocking issue.`,
    `- CSharpier.Core was NOT downgraded or pinned below ${options.toVersion}.`,
    `- No tests were deleted, skipped, or weakened. A test's expected output may only change when CSharpier itself intentionally formats that code differently in ${options.toVersion}; anything else is a blocking issue.`,
    `- \`deployment/schema.json\` only has changes that correspond to defaults CSharpier changed.`,
    `- Nothing was added that can't work after trimming and ahead of time compilation (for example reflection-based serialization), and any new \`ILLink.Substitutions.xml\` entries only replace code the plugin never runs.`,
    `- README.md and the plugin's own version in the project file were NOT modified; flag either as a blocking issue.`,
    `- No obvious correctness bugs, and code style matches the surrounding code.`,
    ``,
    `When done, your FINAL message must be ONLY a JSON object (no markdown code fences, no extra prose) of exactly this shape:`,
    `{"approved": true|false, "summary": "one sentence", "issues": [{"severity": "blocking"|"nit", "description": "..."}]}`,
    `Set approved=false if there is any blocking issue; nits alone do not block.`,
  ].join("\n");
}

function parseReview(message: string): ReviewResult {
  const review = JSON.parse(extractJsonObject(message)) as ReviewResult;
  if (typeof review.approved !== "boolean" || !Array.isArray(review.issues)) {
    throw new Error(`Codex review returned malformed JSON:\n${message}`);
  }
  return review;
}

// Codex is instructed to emit only JSON, but tolerate an accidental code fence
// or surrounding prose by extracting the outermost { ... } object.
function extractJsonObject(text: string): string {
  const start = text.indexOf("{");
  const end = text.lastIndexOf("}");
  if (start === -1 || end === -1 || end < start) {
    throw new Error(`Could not find a JSON object in Codex review output:\n${text}`);
  }
  return text.slice(start, end + 1);
}

function logReview(review: ReviewResult): void {
  $.log(`Review: ${review.approved ? "approved" : "changes requested"} — ${review.summary}`);
  for (const issue of review.issues) {
    $.logLight(`  [${issue.severity}] ${issue.description}`);
  }
}

// setup ------------------------------------------------------------------------

function requireApiKey(): string {
  const apiKey = Deno.env.get("OPENAI_API_KEY");
  if (!apiKey) {
    throw new Error("OPENAI_API_KEY is not set. It is required to run the AI fixer and reviewer.");
  }
  return apiKey;
}

async function ensureCodexInstalled(): Promise<void> {
  const found = await $`codex --version`.noThrow().quiet();
  if (found.code === 0) {
    return;
  }
  $.logStep("Installing OpenAI Codex CLI...");
  await $`npm install -g @openai/codex`;
}

// `codex exec` does not read OPENAI_API_KEY on its own -- it needs credentials
// stored via `codex login` first, otherwise its requests go out with no auth
// header and the API returns 401. The key is piped over stdin so it never
// appears in the process arguments.
async function codexLogin(apiKey: string): Promise<void> {
  $.logStep("Authenticating Codex with the OpenAI API key...");
  await $`codex login --with-api-key`.stdinText(apiKey);
}
