const { exec } = require('child_process');
const { readFile, writeFile, copyFile, mkdir, readdir, rm } =
  require('fs').promises;
const { existsSync } = require('fs');
const { join } = require('path');
const util = require('util');
const { marked } = require('marked');
const execPromise = util.promisify(exec);

const generatedDataDir = './src/data/generated';

async function emptyDirectory(dir: string): Promise<void> {
  try {
    await rm(dir, { recursive: true });
  } catch {}

  await mkdir(dir, { recursive: true });
}

async function buildApiDocs(): Promise<void> {
  const targetDir = './docs/api';
  const dll =
    '../artifacts/bin/DomainMapper.Abstractions/debug/DomainMapper.Abstractions.dll';

  // clean target directory
  await emptyDirectory(targetDir);

  // use xmldoc2md to convert the dotnet xml documentation to markdown
  await execPromise('dotnet tool restore');
  await execPromise(
    `dotnet xmldoc2md ${dll} --member-accessibility-level public --output ${targetDir}`,
  );

  // we instead use the docusaurus generated index
  await rm(join(targetDir, 'index.md'));

  const fileNames = await readdir(targetDir);
  for (const fileName of fileNames) {
    const filePath = join(targetDir, fileName);

    let content = await readFile(filePath, 'utf-8');

    // this replacement is required due to jsx limitations
    content = content.replace(/<br>/g, '<br />');

    // replace local System.*Attribute with MS docs links
    // these are generated as local references since source-generated polyfills are used instead of references
    // but xmldoc2md doesn't include these
    content = content.replace(
      /\.\/system\.(.*?)attribute\.md/g,
      'https://learn.microsoft.com/en-us/dotnet/api/system.$1attribute',
    );

    // xmldoc2md links enum values to fragments, but renders enum fields as a
    // table without anchors. Keep the useful type link and drop dead fragments.
    content = content.replace(
      /(domainmapper\.abstractions\.(?:enumnamingstrategy|ignoreobsoletemembersstrategy|mappingconversiontype|requiredmappingstrategy)\.md)#[a-z0-9-]+/g,
      '$1',
    );

    // add font matter to specify non-escaped title
    // as only < and > are the encoded chars, use replace instead of using a decode dependency
    const title = content.match(/# (.*)/)[1];
    const unescapedTitle = title.replace('&lt;', '<').replace('&gt;', '>');
    content = `---\ntitle: ${unescapedTitle}\n---\n\n${content}`;

    await writeFile(filePath, content);
  }
}

async function buildAnalyzerRulesData(): Promise<void> {
  // extract analyzer rules from AnalyzerReleases.Shipped.md and write to a json file
  const targetFile = join(generatedDataDir, 'analyzer-rules.json');
  const sourceFile = '../src/DomainMapper/AnalyzerReleases.Shipped.md';
  const analyzerDiagnosticsDocsDir =
    './docs/configuration/analyzer-diagnostics';

  let rules = {};
  let removingRules = true;
  const walkTokens = (token) => {
    if (token.type === 'heading' && token.depth === 3) {
      removingRules = token.text === 'Removed Rules';
      return token;
    }

    if (token.type !== 'table') {
      return token;
    }

    for (const row of token.rows) {
      const id = row[0].text;
      if (removingRules) {
        delete rules[id];
        continue;
      }

      rules[id] = {
        id,
        category: row[1].text,
        severity: row[2].text,
        notes: row[3].text,
        hasDocumentation: existsSync(
          join(analyzerDiagnosticsDocsDir, `${id}.mdx`),
        ),
      };
    }

    return token;
  };
  marked.use({ walkTokens });

  const analyzersMd = await readFile(sourceFile);
  marked.parse(analyzersMd.toString());
  await writeFile(
    targetFile,
    JSON.stringify(Object.values(rules), undefined, '  '),
  );
}

async function buildSamples(): Promise<void> {
  const targetDir = join(generatedDataDir, 'samples');
  await mkdir(targetDir);

  // Copy generated mapper to target dir
  const generatedMapperDir =
    '../artifacts/obj/DomainMapper.Sample/debug/generated/DomainMapper/DomainMapper.DomainMapperGenerator';
  const generatedMapperFiles = (await readdir(generatedMapperDir)).filter(
    (fileName) => fileName.endsWith('.g.cs'),
  );
  if (generatedMapperFiles.length !== 1) {
    throw new Error(
      `Expected one generated sample mapper, found ${generatedMapperFiles.length}.`,
    );
  }
  await copyFile(
    join(generatedMapperDir, generatedMapperFiles[0]),
    join(targetDir, 'OrderMapper.g.cs'),
  );

  // Copy sample project files to target dir
  const sampleProject = '../samples/DomainMapper.Sample';
  const projectFilesToCopy = ['OrderMapper.cs', 'Order.cs', 'OrderDraft.cs'];
  for (const file of projectFilesToCopy) {
    await copyFile(join(sampleProject, file), join(targetDir, file));
  }
}

async function buildRobotsTxt(): Promise<void> {
  const targetFile = 'static/robots.txt';
  const content =
    process.env.ENVIRONMENT === 'next'
      ? 'User-agent: *\nDisallow: /\n'
      : 'User-agent: *\n';
  await writeFile(targetFile, content);
}

(async () => {
  await emptyDirectory(generatedDataDir);
  await buildApiDocs();
  await buildAnalyzerRulesData();
  await buildSamples();
  await buildRobotsTxt();
})();
