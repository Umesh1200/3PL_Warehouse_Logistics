import { AppIconName } from '../components/common/AppIcon';

export type FieldType =
  | 'string'
  | 'text'
  | 'integer'
  | 'decimal'
  | 'boolean'
  | 'date'
  | 'datetime'
  | 'select'
  | 'badge'
  | 'link'
  | 'code'
  | 'currency'
  | 'email'
  | 'phone';

export interface SelectOption {
  value: string | number;
  label: string;
  variant?: 'primary' | 'success' | 'warning' | 'danger' | 'info' | 'secondary';
  icon?: AppIconName | string;
}

export interface FieldValidation {
  min?: number;
  max?: number;
  pattern?: string;
  message?: string;
}

export interface FieldDefinition {
  name: string;
  label: string;
  type: FieldType;
  required?: boolean;
  readOnly?: boolean;
  hidden?: boolean;
  defaultValue?: any;
  placeholder?: string;
  options?: SelectOption[];
  validation?: FieldValidation;
  icon?: AppIconName | string;
  span?: number; // 1 to 12
  readPermission?: string;
  writePermission?: string;
  helperText?: string;
}

export interface ListColumnDefinition {
  field: string;
  label?: string; // Optional display override
  width?: string;
  align?: 'left' | 'center' | 'right';
  sortable?: boolean;
  format?: 'code' | 'badge' | 'currency' | 'date' | 'datetime' | 'boolean' | 'text';
}

export interface ListViewDefinition {
  columns: ListColumnDefinition[];
  defaultSort?: {
    field: string;
    direction: 'asc' | 'desc';
  };
  searchFields?: string[];
  emptyState?: {
    icon: AppIconName | string;
    title: string;
    description: string;
    actionLabel?: string;
  };
}

export interface FormSectionDefinition {
  id: string;
  title: string;
  description?: string;
  icon?: AppIconName | string;
  fields: string[]; // Field names
  columns?: number; // 1 or 2 columns layout
}

export interface FormViewDefinition {
  sections: FormSectionDefinition[];
}

export interface ActionDefinition {
  id: string;
  label: string;
  icon?: AppIconName | string;
  variant?: 'primary' | 'secondary' | 'danger' | 'outline';
  target: 'header' | 'row' | 'form';
  permission?: string;
  confirmPrompt?: string;
}

export interface EntityEndpoints {
  list: string;
  get?: string;
  create?: string;
  update?: string;
  delete?: string;
}

export interface EntityPermissions {
  view: string;
  create?: string;
  edit?: string;
  delete?: string;
}

export interface EntitySchema {
  name: string;
  version: string;
  title: string;
  pluralTitle: string;
  description?: string;
  icon: AppIconName | string;
  primaryKey: string;
  endpoints: EntityEndpoints;
  permissions: EntityPermissions;
  fields: FieldDefinition[];
  views: {
    list: ListViewDefinition;
    form?: FormViewDefinition;
  };
  actions: ActionDefinition[];
}
