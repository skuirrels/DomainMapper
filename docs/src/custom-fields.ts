export interface CustomFields {
  domainMapVersion: string;
  environment: {
    name: string;
    stable: boolean;
    next: boolean;
    local: boolean;
  };
}
