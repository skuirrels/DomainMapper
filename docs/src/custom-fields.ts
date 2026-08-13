export interface CustomFields {
  domainMapperVersion: string;
  environment: {
    name: string;
    stable: boolean;
    next: boolean;
    local: boolean;
  };
}
