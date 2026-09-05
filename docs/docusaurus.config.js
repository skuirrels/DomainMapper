// @ts-check
/** @typedef {import('@docusaurus/types').Config} Config */

const fs = require('fs');
const path = require('path');
const { themes } = require('prism-react-renderer');

// The development version is owned by Directory.Build.props at the repository root.
const buildProps = fs.readFileSync(
  path.join(__dirname, '..', 'Directory.Build.props'),
  'utf8',
);
const developmentVersion = /<Version>([^<]+)<\/Version>/.exec(buildProps)?.[1];
const domainMapperVersion =
  process.env.DOMAINMAPPER_VERSION || developmentVersion || 'dev';
const environment = process.env.ENVIRONMENT || 'local';

/** @type {import('./src/custom-fields').CustomFields} */
const customFields = {
  domainMapperVersion,
  environment: {
    name: environment,
    stable: environment === 'stable',
    next: environment === 'next',
    local: environment === 'local',
  },
};

async function createConfig() {
  const rehypeFaq = (await import('./src/plugins/rehype/rehype-faq/index.js'))
    .default;

  /** @type {Config} */
  return {
    customFields,
    title: 'DomainMapper',
    tagline: 'Map data. Preserve intent. A DDD-first .NET source generator.',
    url: process.env.DOCUSAURUS_URL || 'http://localhost:3000',
    baseUrl: process.env.DOCUSAURUS_BASE_URL || '/',
    trailingSlash: true,
    onBrokenAnchors: 'throw',
    favicon: 'img/logo.svg',
    organizationName: 'domainmapper',
    projectName: 'domainmapper',
    markdown: {
      hooks: {
        onBrokenMarkdownLinks: 'throw',
        onBrokenMarkdownImages: 'throw',
      },
    },
    i18n: {
      defaultLocale: 'en',
      locales: ['en'],
    },
    presets: [
      [
        'classic',
        /** @type {import('@docusaurus/preset-classic').Options} */
        ({
          docs: {
            sidebarPath: require.resolve('./sidebars.js'),
            rehypePlugins: [rehypeFaq],
          },
          theme: {
            customCss: require.resolve('./src/css/custom.css'),
          },
        }),
      ],
    ],

    themeConfig:
      /** @type {import('@docusaurus/preset-classic').ThemeConfig} */
      ({
        metadata: [
          {
            name: 'keywords',
            content: '.NET, SourceGenerator, Mapping, Roslyn, dotnet',
          },
        ],
        colorMode: {
          disableSwitch: true,
        },
        navbar: {
          title: 'DomainMapper',
          logo: {
            alt: 'DomainMapper Logo',
            src: 'img/logo.svg',
          },
          items: [
            {
              type: 'doc',
              docId: 'intro',
              position: 'left',
              label: 'Documentation',
              sidebarId: 'docs',
            },
            {
              type: 'doc',
              docId: '/category/api',
              position: 'left',
              label: 'API',
              sidebarId: 'api',
            },
            {
              type: 'doc',
              docId: 'contributing/index',
              position: 'left',
              label: 'Contributing',
              sidebarId: 'contributing',
            },
            {
              type: 'custom-versionsNavbarItem',
              position: 'right',
            },
          ],
        },
        footer: {
          style: 'dark',
          copyright:
            'Copyright © 2026 DomainMapper contributors. Licensed under Apache-2.0.',
          links: [
            {
              title: 'Docs',
              items: [
                {
                  label: 'Introduction',
                  to: '/docs/intro',
                },
                {
                  label: 'Installation',
                  to: '/docs/getting-started/first-mapper',
                },
                {
                  label: 'Configuration',
                  to: '/docs/category/usage-and-configuration',
                },
              ],
            },
            {
              title: 'Community',
              items: [
                {
                  label: 'Contributing',
                  to: '/docs/contributing',
                },
              ],
            },
          ],
        },
        prism: {
          theme: themes.github,
          darkTheme: themes.dracula,
          additionalLanguages: ['csharp', 'powershell', 'editorconfig', 'bash'],
        },
      }),
    plugins: [
      [
        '@docusaurus/plugin-ideal-image',
        /** @type {import('@docusaurus/plugin-ideal-image').PluginOptions} */
        ({
          max: 1600,
          min: 400,
          // Use false to debug, but it incurs huge perf costs
          disableInDev: true,
        }),
      ],
      '@easyops-cn/docusaurus-search-local',
    ],
  };
}

module.exports = createConfig;
