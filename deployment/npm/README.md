# @dprint/csharpier

npm distribution of [dprint-plugin-csharpier](https://github.com/dprint/dprint-plugin-csharpier), which formats C# and XML code with [CSharpier](https://github.com/belav/csharpier).

## Use with the dprint CLI

Requires [dprint](https://dprint.dev/install/) 0.61.0 or later.

Run `dprint add csharpier` or add the package to the `"plugins"` array of a dprint configuration file:

```jsonc
{
  // ...etc...
  "plugins": [
    "npm:@dprint/csharpier@<version>"
  ]
}
```

## Use with @dprint/formatter

Requires [@dprint/formatter](https://github.com/dprint/js-formatter) 0.6.0 or later.

```ts
import { getPath } from "@dprint/csharpier";
import { createFromBuffer } from "@dprint/formatter";
import * as fs from "fs";

const buffer = fs.readFileSync(getPath());
const formatter = createFromBuffer(buffer);

console.log(formatter.formatText({
  filePath: "file.cs",
  fileText: "namespace Test    {   class A{}  }",
}));
```
