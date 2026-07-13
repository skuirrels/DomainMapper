import React from 'react';
import { JSX } from 'react';
import DropdownNavbarItem from '@theme/NavbarItem/DropdownNavbarItem';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import styles from './styles.module.css';
import type { LinkLikeNavbarItemProps } from '@theme/NavbarItem';
import { CustomFields } from '@site/src/custom-fields';

export default function VersionsNavbarItem(): JSX.Element {
  const { environment, domainMapVersion } = useDocusaurusContext().siteConfig
    .customFields as unknown as CustomFields;

  const items: LinkLikeNavbarItemProps[] = [
    {
      label: 'stable',
      to: environment.stable ? '#' : '/',
      isActive: () => environment.stable,
    },
    {
      label: 'next',
      to: environment.next ? '#' : '/',
      isActive: () => environment.next,
    },
  ];

  if (environment.local) {
    items.push({
      label: 'local',
      to: '#',
      isActive: () => true,
    });
  }

  return (
    <DropdownNavbarItem
      label={<>{domainMapVersion}</>}
      items={items}
      className={environment.stable ? undefined : styles.versionAlert}
    />
  );
}
