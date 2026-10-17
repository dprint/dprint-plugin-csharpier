// @ts-check
const assert = require("assert");
const createFromBuffer = require("@dprint/formatter").createFromBuffer;
const getPath = require("./index").getPath;

const buffer = require("fs").readFileSync(getPath());
const formatter = createFromBuffer(buffer);

assert.strictEqual(
  formatter.formatText({
    filePath: "file.cs",
    fileText: "namespace Test    {   class A{}  }\n",
  }),
  "namespace Test\n{\n    class A { }\n}\n",
);
assert.strictEqual(
  formatter.formatText({
    filePath: "file.csproj",
    fileText: "<Project><PropertyGroup></PropertyGroup></Project>\n",
  }),
  "<Project>\n  <PropertyGroup></PropertyGroup>\n</Project>\n",
);
