import React, { useId } from 'react';

interface OutboundTableRegionProps {
  ariaLabel: string;
  hintText?: string;
  scrollClassName?: string;
  hintTestId?: string;
  regionTestId?: string;
  children: React.ReactNode;
}

/**
 * Accessible horizontal-scroll wrapper for wide outbound data tables.
 * Exposes a keyboard-focusable scroll region linked via aria-describedby to a
 * compact mobile/320px scroll discoverability hint without altering desktop layout.
 */
export const OutboundTableRegion: React.FC<OutboundTableRegionProps> = ({
  ariaLabel,
  hintText = 'Swipe or scroll horizontally to view all columns →',
  scrollClassName = 'outbound-table-wrap',
  hintTestId,
  regionTestId,
  children,
}) => {
  const hintId = useId();

  return (
    <div className="outbound-table-region">
      <p id={hintId} className="table-scroll-hint" data-testid={hintTestId}>
        {hintText}
      </p>
      <div
        className={scrollClassName}
        role="region"
        aria-label={ariaLabel}
        aria-describedby={hintId}
        tabIndex={0}
        data-testid={regionTestId}
      >
        {children}
      </div>
    </div>
  );
};
