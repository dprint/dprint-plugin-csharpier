import $ from "jsr:@david/dax@^0.47.0";
import { generateChangeLog } from "jsr:@dprint/automation@0.11.3";
import * as semver from "jsr:@std/semver@1";

const version = Deno.args[0];
// the change log is generated from the commits since the previous version, which the first release doesn't have
const changelog = await hasPastVersion()
  ? await generateChangeLog({
    versionTo: version,
  })
  : "Initial release.";
const text = `## Changes

${changelog}

## Install

1. Run \`dprint add csharpier\`, which will add the plugin to your dprint configuration file.
2. Add a "csharpier" configuration property if desired.
   \`\`\`jsonc
   {
     // ...etc...
     "csharpier": {
       "lineWidth": 120,
       "xml.indentWidth": 4
     }
   }
   \`\`\`
`;

console.log(text);

async function hasPastVersion() {
  await $`git fetch origin --tags --recurse-submodules=no`.quiet();
  const newVersion = semver.parse(version);
  const tags = await $`git tag`.lines();
  return tags.some((tag) => {
    const tagVersion = semver.tryParse(tag);
    return tagVersion != null && semver.greaterThan(newVersion, tagVersion);
  });
}
