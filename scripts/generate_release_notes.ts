import { generateChangeLog } from "jsr:@dprint/automation@0.11.3";

const version = Deno.args[0];
const changelog = await generateChangeLog({
  versionTo: version,
});
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
