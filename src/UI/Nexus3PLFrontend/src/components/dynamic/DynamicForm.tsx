import React, { useState, useContext } from 'react';
import { EntitySchema, FieldDefinition } from '../../types/schema';
import { AppIcon } from '../common/AppIcon';
import { AuthContext } from '../../context/AuthContext';

export interface DynamicFormProps {
  schema: EntitySchema;
  initialData?: Record<string, any>;
  onSubmit: (formData: Record<string, any>) => Promise<void> | void;
  onCancel?: () => void;
  isSubmitting?: boolean;
  errorMessage?: string | null;
  mode?: 'create' | 'edit';
}

export const DynamicForm: React.FC<DynamicFormProps> = ({
  schema,
  initialData = {},
  onSubmit,
  onCancel,
  isSubmitting = false,
  errorMessage,
  mode = 'create',
}) => {
  // Initialize form state with initialData or field default values
  const [formData, setFormData] = useState<Record<string, any>>(() => {
    const initial: Record<string, any> = {};
    for (const field of schema.fields) {
      if (initialData[field.name] !== undefined) {
        initial[field.name] = initialData[field.name];
      } else if (field.defaultValue !== undefined) {
        initial[field.name] = field.defaultValue;
      } else {
        initial[field.name] = field.type === 'boolean' ? false : '';
      }
    }
    return initial;
  });

  const [validationErrors, setValidationErrors] = useState<Record<string, string>>({});

  const validateField = (field: FieldDefinition, value: any): string | null => {
    if (field.hidden) return null;
    if (field.readOnly && mode === 'edit') return null;

    // Required check
    if (field.required) {
      if (value === undefined || value === null || (typeof value === 'string' && value.trim() === '')) {
        return `${field.label} is required.`;
      }
    }

    if (value !== undefined && value !== null && value !== '') {
      const valStr = String(value);

      // Min/Max string length
      if (field.validation?.min !== undefined && valStr.length < field.validation.min) {
        return field.validation.message || `${field.label} must be at least ${field.validation.min} characters.`;
      }
      if (field.validation?.max !== undefined && valStr.length > field.validation.max) {
        return field.validation.message || `${field.label} cannot exceed ${field.validation.max} characters.`;
      }

      // Regex pattern
      if (field.validation?.pattern) {
        const regex = new RegExp(field.validation.pattern);
        if (!regex.test(valStr)) {
          return field.validation.message || `${field.label} format is invalid.`;
        }
      }

      // Email format
      if (field.type === 'email') {
        const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
        if (!emailRegex.test(valStr)) {
          return 'Please enter a valid email address.';
        }
      }
    }

    return null;
  };

  const handleFieldChange = (field: FieldDefinition, rawValue: any) => {
    let value = rawValue;

    // Uppercase normalization for code and currency fields
    if (field.type === 'code' || field.type === 'currency') {
      if (typeof value === 'string') {
        value = value.toUpperCase();
      }
    }

    setFormData((prev) => ({ ...prev, [field.name]: value }));

    // Real-time validation
    const error = validateField(field, value);
    setValidationErrors((prev) => {
      const updated = { ...prev };
      if (error) {
        updated[field.name] = error;
      } else {
        delete updated[field.name];
      }
      return updated;
    });
  };

  const auth = useContext(AuthContext);
  const isSuperAdmin = auth?.user?.roles?.some((r) => r.toLowerCase() === 'superadmin') ?? false;

  const checkPermission = (perm?: string): boolean => {
    if (!perm) return true;
    if (!auth || !auth.user) return false;
    if (isSuperAdmin) return true;
    return auth.hasPermission(perm);
  };

  const fieldMap = new Map(schema.fields.map((f) => [f.name, f]));

  // Determine sections, filtering out hidden and permission-restricted fields
  const rawSections = schema.views.form?.sections || [
    {
      id: 'default',
      title: `${schema.title} Details`,
      fields: schema.fields.filter((f) => !f.hidden).map((f) => f.name),
      columns: 2,
    },
  ];

  const visibleSections = rawSections
    .map((sec) => ({
      ...sec,
      fields: sec.fields.filter((fName) => {
        const field = fieldMap.get(fName);
        if (!field || field.hidden) return false;
        if (field.readPermission && !checkPermission(field.readPermission)) return false;
        return true;
      }),
    }))
    .filter((sec) => sec.fields.length > 0);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (isSubmitting) return;

    // Run full validation on visible, permitted fields
    const errors: Record<string, string> = {};
    const sanitizedData: Record<string, any> = {};

    for (const sec of visibleSections) {
      for (const fName of sec.fields) {
        const field = fieldMap.get(fName);
        if (field) {
          sanitizedData[fName] = formData[fName];
          const err = validateField(field, formData[fName]);
          if (err) {
            errors[fName] = err;
          }
        }
      }
    }

    if (Object.keys(errors).length > 0) {
      setValidationErrors(errors);
      return;
    }

    onSubmit(sanitizedData);
  };

  const renderInput = (field: FieldDefinition) => {
    const value = formData[field.name] ?? '';
    const error = validationErrors[field.name];
    const isFieldReadOnly =
      field.readOnly ||
      (mode === 'edit' && field.name === 'accountCode') ||
      (!!field.writePermission && !checkPermission(field.writePermission));

    const commonProps = {
      id: `field-${field.name}`,
      name: field.name,
      disabled: isFieldReadOnly || isSubmitting,
      'aria-invalid': !!error,
      'aria-describedby': error ? `error-${field.name}` : undefined,
    };

    switch (field.type) {
      case 'select':
        return (
          <select
            {...commonProps}
            className={`form-select ${error ? 'input-error' : ''}`}
            value={value}
            onChange={(e) => {
              const val = e.target.value;
              // Parse number if option values are numeric
              const numVal = Number(val);
              handleFieldChange(field, isNaN(numVal) ? val : numVal);
            }}
          >
            {field.options?.map((opt) => (
              <option key={String(opt.value)} value={opt.value}>
                {opt.label}
              </option>
            ))}
          </select>
        );

      case 'text':
        return (
          <textarea
            {...commonProps}
            className={`form-textarea ${error ? 'input-error' : ''}`}
            rows={3}
            placeholder={field.placeholder}
            value={value}
            onChange={(e) => handleFieldChange(field, e.target.value)}
          />
        );

      case 'boolean':
        return (
          <label className="checkbox-label">
            <input
              {...commonProps}
              type="checkbox"
              className="form-checkbox"
              checked={!!value}
              onChange={(e) => handleFieldChange(field, e.target.checked)}
            />
            <span>{field.placeholder || 'Enable'}</span>
          </label>
        );

      case 'code':
        return (
          <input
            {...commonProps}
            type="text"
            className={`form-input code-font ${error ? 'input-error' : ''}`}
            placeholder={field.placeholder}
            value={value}
            onChange={(e) => handleFieldChange(field, e.target.value)}
          />
        );

      case 'currency':
        return (
          <input
            {...commonProps}
            type="text"
            maxLength={3}
            className={`form-input code-font currency-input ${error ? 'input-error' : ''}`}
            placeholder={field.placeholder || 'USD'}
            value={value}
            onChange={(e) => handleFieldChange(field, e.target.value)}
          />
        );

      case 'integer':
      case 'decimal':
        return (
          <input
            {...commonProps}
            type="number"
            step={field.type === 'decimal' ? '0.01' : '1'}
            className={`form-input ${error ? 'input-error' : ''}`}
            placeholder={field.placeholder}
            value={value}
            onChange={(e) => handleFieldChange(field, e.target.valueAsNumber || 0)}
          />
        );

      case 'email':
        return (
          <input
            {...commonProps}
            type="email"
            className={`form-input ${error ? 'input-error' : ''}`}
            placeholder={field.placeholder}
            value={value}
            onChange={(e) => handleFieldChange(field, e.target.value)}
          />
        );

      case 'phone':
        return (
          <input
            {...commonProps}
            type="tel"
            className={`form-input ${error ? 'input-error' : ''}`}
            placeholder={field.placeholder}
            value={value}
            onChange={(e) => handleFieldChange(field, e.target.value)}
          />
        );

      default:
        return (
          <input
            {...commonProps}
            type="text"
            className={`form-input ${error ? 'input-error' : ''}`}
            placeholder={field.placeholder}
            value={value}
            onChange={(e) => handleFieldChange(field, e.target.value)}
          />
        );
    }
  };

  return (
    <form className="dynamic-form" onSubmit={handleSubmit} noValidate>
      {errorMessage && (
        <div className="form-alert-error" role="alert">
          <AppIcon name="AlertCircle" size="sm" color="danger" />
          <span>{errorMessage}</span>
        </div>
      )}

      {visibleSections.map((section) => {
        const sectionFields = section.fields
          .map((fname) => fieldMap.get(fname))
          .filter((f): f is FieldDefinition => !!f && !f.hidden);

        if (sectionFields.length === 0) return null;

        const gridClass = section.columns === 1 ? 'grid-1-col' : 'grid-2-col';

        return (
          <fieldset key={section.id} className="form-section">
            <legend className="section-legend">
              {section.icon && <AppIcon name={section.icon} size="sm" color="primary" />}
              <span>{section.title}</span>
            </legend>
            {section.description && <p className="section-desc">{section.description}</p>}

            <div className={`section-fields ${gridClass}`}>
              {sectionFields.map((field) => {
                const error = validationErrors[field.name];

                return (
                  <div key={field.name} className={`form-field-group ${field.span === 12 ? 'col-span-full' : ''}`}>
                    <label htmlFor={`field-${field.name}`} className="field-label">
                      {field.icon && <AppIcon name={field.icon} size="xs" color="muted" />}
                      <span>{field.label}</span>
                      {field.required && <span className="required-star">*</span>}
                    </label>

                    {renderInput(field)}

                    {field.helperText && !error && (
                      <span className="field-helper">{field.helperText}</span>
                    )}

                    {error && (
                      <span id={`error-${field.name}`} className="field-error-msg" role="alert">
                        {error}
                      </span>
                    )}
                  </div>
                );
              })}
            </div>
          </fieldset>
        );
      })}

      <div className="form-sticky-footer">
        {onCancel && (
          <button
            type="button"
            className="btn btn-secondary"
            onClick={onCancel}
            disabled={isSubmitting}
          >
            <AppIcon name="X" size="sm" />
            <span>Cancel</span>
          </button>
        )}
        <button
          type="submit"
          className="btn btn-primary"
          disabled={isSubmitting}
          data-testid="submit-form-btn"
        >
          <AppIcon name={isSubmitting ? 'RefreshCw' : 'Check'} size="sm" />
          <span>
            {isSubmitting
              ? 'Saving...'
              : mode === 'create'
              ? `Create ${schema.title}`
              : 'Save Changes'}
          </span>
        </button>
      </div>

      <style>{`
        .dynamic-form {
          display: flex;
          flex-direction: column;
          gap: 20px;
        }
        .form-alert-error {
          display: flex;
          align-items: center;
          gap: 10px;
          padding: 12px 16px;
          background: var(--status-danger-bg);
          border: 1px solid var(--status-danger-border);
          color: var(--status-danger-text);
          border-radius: var(--radius-sm);
          font-size: 13.5px;
        }
        .form-section {
          border: 1px solid var(--border-subtle);
          border-radius: var(--radius-sm);
          padding: 18px 20px;
          margin: 0;
          background: #ffffff;
        }
        .section-legend {
          display: flex;
          align-items: center;
          gap: 8px;
          font-size: 14px;
          font-weight: 700;
          color: var(--text-main);
          padding: 0 6px;
        }
        .section-desc {
          font-size: 12.5px;
          color: var(--text-muted);
          margin: 4px 0 16px;
        }
        .section-fields {
          display: grid;
          gap: 16px;
        }
        .grid-1-col {
          grid-template-columns: 1fr;
        }
        .grid-2-col {
          grid-template-columns: 1fr 1fr;
        }
        @media (max-width: 640px) {
          .grid-2-col {
            grid-template-columns: 1fr;
          }
        }
        .form-field-group {
          display: flex;
          flex-direction: column;
          gap: 6px;
        }
        .col-span-full {
          grid-column: 1 / -1;
        }
        .field-label {
          display: flex;
          align-items: center;
          gap: 6px;
          font-size: 13px;
          font-weight: 650;
          color: var(--text-main);
        }
        .required-star {
          color: var(--status-danger-text);
          font-weight: 700;
        }
        .form-input, .form-select, .form-textarea {
          width: 100%;
          padding: 9px 12px;
          border-radius: var(--radius-sm);
          border: 1px solid var(--border-actionable);
          font-family: inherit;
          font-size: 13.5px;
          color: var(--text-main);
          background: #ffffff;
          transition: border-color var(--transition-fast), box-shadow var(--transition-fast);
        }
        .form-input:focus, .form-select:focus, .form-textarea:focus {
          outline: none;
          border-color: var(--brand-primary);
          box-shadow: 0 0 0 3px rgba(82, 84, 230, 0.15);
        }
        .form-input:disabled, .form-select:disabled, .form-textarea:disabled {
          background: #f1f5f9;
          color: var(--text-muted);
          cursor: not-allowed;
          border-color: #cbd5e1;
        }
        .input-error {
          border-color: var(--status-danger-border) !important;
          background: #fffafa !important;
        }
        .code-font {
          font-family: 'JetBrains Mono', 'SF Mono', Consolas, monospace;
          font-weight: 600;
          letter-spacing: 0.05em;
        }
        .currency-input {
          text-transform: uppercase;
        }
        .field-helper {
          font-size: 11.5px;
          color: var(--text-muted);
        }
        .field-error-msg {
          font-size: 12px;
          font-weight: 600;
          color: var(--status-danger-text);
        }
        .checkbox-label {
          display: flex;
          align-items: center;
          gap: 8px;
          font-size: 13.5px;
          cursor: pointer;
          margin-top: 6px;
        }
        .form-checkbox {
          width: 18px;
          height: 18px;
          cursor: pointer;
        }
        .form-sticky-footer {
          display: flex;
          align-items: center;
          justify-content: flex-end;
          gap: 12px;
          padding-top: 16px;
          border-top: 1px solid var(--border-subtle);
        }
      `}</style>
    </form>
  );
};
