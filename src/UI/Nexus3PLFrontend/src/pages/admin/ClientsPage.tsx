import React from 'react';
import { PermissionGuard } from '../../components/auth/PermissionGuard';
import { DynamicEntityPage } from '../../components/dynamic/DynamicEntityPage';

/**
 * Metadata-driven Clients module screen.
 * Column definitions, labels, order, actions, validation, and forms
 * are completely driven by the authoritative EntitySchema (client.json),
 * requiring zero JSX modifications to change presentation attributes.
 */
export const ClientsPage: React.FC = () => {
  return (
    <PermissionGuard requiredPermission="clients:view">
      <DynamicEntityPage entityName="Client" />
    </PermissionGuard>
  );
};
