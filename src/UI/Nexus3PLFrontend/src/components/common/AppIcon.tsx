import React from 'react';
import {
  // Modules / Navigation
  LayoutDashboard,
  Compass,
  ArrowDownToLine,
  PackageOpen,
  Inbox,
  PackageCheck,
  ArrowDownRight,
  Boxes,
  Warehouse,
  Building2,
  Building,
  Users,
  ShieldCheck,
  Lock,
  KeyRound,
  Truck,
  Navigation,
  BarChart3,
  TrendingUp,
  Settings,
  Sliders,
  Send,
  Package,
  Receipt,
  CreditCard,
  // Actions & Controls
  Search,
  Plus,
  RefreshCw,
  Edit2,
  Trash2,
  Check,
  X,
  ChevronRight,
  ChevronDown,
  ChevronLeft,
  ChevronUp,
  Filter,
  ArrowUpDown,
  ExternalLink,
  Eye,
  Download,
  Upload,
  AlertTriangle,
  AlertCircle,
  CheckCircle2,
  Info,
  Menu,
  LogOut,
  Barcode,
  QrCode,
  Sparkles,
  Clock,
  Calendar,
  DollarSign,
  MapPin,
  Phone,
  Mail,
  Layers,
  HelpCircle,
  LucideIcon,
} from 'lucide-react';

/**
 * Authoritative allowlist of supported icon keys across modules, views, fields, and actions.
 * Dynamic eval, raw URLs, or arbitrary SVG strings from tenant metadata are strictly prohibited.
 */
export const ICON_REGISTRY = {
  // Navigation & Modules
  LayoutDashboard,
  Compass,
  ArrowDownToLine,
  PackageOpen,
  Inbox,
  PackageCheck,
  ArrowDownRight,
  Boxes,
  Warehouse,
  Building2,
  Building,
  Users,
  ShieldCheck,
  Lock,
  KeyRound,
  Truck,
  Navigation,
  BarChart3,
  TrendingUp,
  Settings,
  Sliders,
  Send,
  Package,
  Receipt,
  CreditCard,

  // Actions & Floor Controls
  Search,
  Plus,
  RefreshCw,
  Edit2,
  Trash2,
  Check,
  X,
  ChevronRight,
  ChevronDown,
  ChevronLeft,
  ChevronUp,
  Filter,
  ArrowUpDown,
  ExternalLink,
  Eye,
  Download,
  Upload,
  AlertTriangle,
  AlertCircle,
  CheckCircle2,
  Info,
  Menu,
  LogOut,
  Barcode,
  QrCode,
  Sparkles,
  Clock,
  Calendar,
  DollarSign,
  MapPin,
  Phone,
  Mail,
  Layers,
  HelpCircle,
} as const satisfies Record<string, LucideIcon>;

export type AppIconName = keyof typeof ICON_REGISTRY;

export type AppIconSize = 'xs' | 'sm' | 'md' | 'lg' | 'xl' | number;

export type AppIconColor =
  | 'primary'
  | 'muted'
  | 'success'
  | 'warning'
  | 'danger'
  | 'info'
  | 'inverse'
  | 'current'
  | string;

export interface AppIconProps {
  name: AppIconName | (string & {});
  size?: AppIconSize;
  color?: AppIconColor;
  strokeWidth?: number;
  className?: string;
  label?: string; // If supplied, renders role="img" with aria-label; else aria-hidden="true"
  title?: string;
  style?: React.CSSProperties;
}

const SIZE_MAP: Record<string, number> = {
  xs: 14,
  sm: 16,
  md: 20,
  lg: 24,
  xl: 32,
};

const COLOR_MAP: Record<string, string> = {
  primary: 'var(--brand-primary)',
  muted: 'var(--text-muted)',
  success: 'var(--status-success-text)',
  warning: 'var(--status-warning-text)',
  danger: 'var(--status-danger-text)',
  info: 'var(--status-info-text)',
  inverse: 'var(--text-inverse)',
  current: 'currentColor',
};

export const AppIcon: React.FC<AppIconProps> = ({
  name,
  size = 'sm',
  color = 'current',
  strokeWidth = 1.75,
  className = '',
  label,
  title,
  style,
}) => {
  const IconComponent = (hasAppIcon(name) ? ICON_REGISTRY[name] : undefined) || HelpCircle;

  const resolvedSize = typeof size === 'number' ? size : SIZE_MAP[size] || 16;
  const resolvedColor = COLOR_MAP[color] || color;

  const accessibilityProps = label
    ? { role: 'img', 'aria-label': label, 'aria-hidden': false }
    : { 'aria-hidden': true };

  return (
    <IconComponent
      size={resolvedSize}
      color={resolvedColor}
      strokeWidth={strokeWidth}
      className={`app-icon ${className}`.trim()}
      style={style}
      {...accessibilityProps}
    >
      {title && <title>{title}</title>}
    </IconComponent>
  );
};

export const hasAppIcon = (name: string): name is AppIconName => {
  return Object.prototype.hasOwnProperty.call(ICON_REGISTRY, name);
};
