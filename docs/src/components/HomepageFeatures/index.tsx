import React from 'react';
import { JSX } from 'react';
import clsx from 'clsx';
import styles from './styles.module.css';
import Heading from '@theme/Heading';
import EasyToUseIcon from '@site/static/img/easy-to-use.svg';
import FastReadableIcon from '@site/static/img/fast-reliable.svg';
import PoweredByIcon from '@site/static/img/powered-by.svg';

type FeatureItem = {
  title: string;
  Svg: React.ComponentType<React.ComponentProps<'svg'>>;
  description: JSX.Element;
};

const FeatureList: FeatureItem[] = [
  {
    title: 'Domain boundaries first',
    Svg: EasyToUseIcon,
    description: (
      <>
        Map commands and contracts through constructors or named factories while
        keeping validation and invariants in user-owned domain code.
      </>
    ),
  },
  {
    title: 'Readable by construction',
    Svg: FastReadableIcon,
    description: (
      <>
        Generated C# is intentionally inspectable and debuggable. There is no
        reflection-driven runtime mapping plan to reverse engineer.
      </>
    ),
  },
  {
    title: 'Compile-time feedback',
    Svg: PoweredByIcon,
    description: (
      <>
        Roslyn diagnostics expose missing or ambiguous mappings during the
        build, while generated code remains trimming-safe and AOT-friendly.
      </>
    ),
  },
];

function Feature({ title, Svg, description }: FeatureItem) {
  return (
    <div className={clsx('col col--4')}>
      <div className="text--center">
        <Svg className={styles.featureSvg} role="img" />
      </div>
      <div className="text--center padding-horiz--md">
        <Heading as={'h3'}>{title}</Heading>
        <p>{description}</p>
      </div>
    </div>
  );
}

export default function HomepageFeatures(): JSX.Element {
  return (
    <section className={styles.features}>
      <div className="container">
        <div className="row">
          {FeatureList.map((props, idx) => (
            <Feature key={idx} {...props} />
          ))}
        </div>
      </div>
    </section>
  );
}
