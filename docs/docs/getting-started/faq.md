---
sidebar_position: 4
description: Frequently asked questions and answers.
title: FAQ
---

<!-- if updated, make sure the comment in plugins/rehype/rehype-faq/index.js is considered  -->

# Frequently asked questions and answers {#faq}

Here you can find answers to frequently asked questions and common problems about DomainMap.

## DomainMap does not work when I use source generator X.

Chaining source generators is not supported by Roslyn.

## I updated the DomainMap version, but the generated code still looks the same.

Restart the IDE to make it load the new version of DomainMap. This is a bug of the IDE.

## Everything is configured correctly and dotnet build works, but the IDE shows the error "[Mapper method] must have an implementation part because it has accessibility modifiers"

Make sure your project meets the [requirements](./installation.mdx#requirements).
Try rebuilding the solution or restarting the IDE. This is a bug in the IDE.

## My advanced use case isn't supported by DomainMap or needs lots of configuration. What should I do?

Write the mapping for that class manually. You can mix automatically generated mappings and [user implemented mappings](../configuration/user-implemented-methods.mdx) without problems.

## My code throws `FileNotFoundException` with `DomainMap.Abstractions`. What should I do?

Are you using [reference handling](../configuration/reference-handling.md)
or have you enabled the [preservation of DomainMap attributes at runtime](installation.mdx#preserving-the-attributes-at-runtime)?
Make sure `ExcludeAssets` on the `PackageReference` does not include `runtime` as these features require runtime assets.

## Is DomainMap supported by the Mapperly maintainers?

No. DomainMap is an independent derivative and is not affiliated with or endorsed by the Mapperly maintainers. Report DomainMap problems in the repository where this source is published rather than in Mapperly's issue tracker.
