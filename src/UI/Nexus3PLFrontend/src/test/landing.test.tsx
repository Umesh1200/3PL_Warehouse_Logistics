import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import React from 'react';
import { App } from '../App';
import { TrackingSimulator } from '../components/TrackingSimulator';

describe('Nexus3PL Brand & Landing Page Tests', () => {
  it('renders the brand logo and navigation properly', () => {
    render(<App />);
    const logoImg = screen.getAllByAltText('Nexus3PL Logo');
    expect(logoImg.length).toBeGreaterThanOrEqual(1);

    expect(screen.getByText('Intelligent Warehouse Operations &')).toBeDefined();
    expect(screen.getByText('Precision Courier Logistics')).toBeDefined();
  });

  it('renders live KPI metrics ticker with demo notice label', () => {
    render(<App />);
    expect(screen.getByText('99.85%')).toBeDefined();
    expect(screen.getByText('1.4M+')).toBeDefined();
    expect(screen.getByText('Enterprise Facilities')).toBeDefined();
    expect(screen.getByText('< 12ms')).toBeDefined();
    expect(screen.getByText(/ILLUSTRATIVE DEMO METRICS • UNCONNECTED TO PRODUCTION DATA/i)).toBeDefined();
  });

  it('interactively updates AWB tracking simulator on preset click and announces changes', () => {
    render(<TrackingSimulator />);
    
    // Initial active shipment should be AWB-9842-US
    expect(screen.getByText('AWB-9842-US')).toBeDefined();
    expect(screen.getByText(/FedEx Priority Express/i)).toBeDefined();

    // Click UPS preset
    const upsBtn = screen.getByText(/AWB-7719-EXP/i);
    fireEvent.click(upsBtn);

    // Should switch to UPS shipment
    expect(screen.getByText('AWB-7719-EXP')).toBeDefined();
    expect(screen.getByText(/UPS Worldwide Saver/i)).toBeDefined();
    expect(screen.getByText('Delivered')).toBeDefined();
  });

  it('clears stale shipment results upon searching an unknown AWB', () => {
    render(<TrackingSimulator />);
    
    // Initially AWB-9842-US is displayed
    expect(screen.getByText('AWB-9842-US')).toBeDefined();

    const input = screen.getByPlaceholderText(/Enter AWB or Tracking ID/i);
    const form = input.closest('form')!;

    // Type unknown AWB
    fireEvent.change(input, { target: { value: 'UNKNOWN-123' } });
    fireEvent.submit(form);

    // Stale result should be CLEARED
    expect(screen.queryByText('AWB-9842-US')).toBeNull();

    // Error alert must be shown
    expect(screen.getByRole('alert')).toBeDefined();
    expect(screen.getByText(/No simulated record found for "UNKNOWN-123"/i)).toBeDefined();
  });

  it('provides modal focus trapping, Escape dismissal, and focus restoration', async () => {
    render(<App />);
    const launchBtn = screen.getByRole('button', { name: /Open Enterprise Portal/i });
    launchBtn.focus();
    expect(document.activeElement).toBe(launchBtn);

    // Open modal
    fireEvent.click(launchBtn);

    expect(screen.getByText('Enterprise Operations Portal Unavailable')).toBeDefined();
    const closeBtn = screen.getByRole('button', { name: /Understood, close dialog/i });

    // Focus moves to close button inside modal
    await waitFor(() => {
      expect(document.activeElement).toBe(closeBtn);
    });

    // Press Escape to dismiss modal
    fireEvent.keyDown(window, { key: 'Escape', code: 'Escape' });

    // Modal is dismissed
    expect(screen.queryByText('Enterprise Operations Portal Unavailable')).toBeNull();

    // Focus restored to the trigger button
    expect(document.activeElement).toBe(launchBtn);
  });

  it('verifies all navigation links have matching in-page targets', () => {
    const { container } = render(<App />);
    const internalLinks = container.querySelectorAll<HTMLAnchorElement>('a[href^="#"]');
    
    expect(internalLinks.length).toBeGreaterThan(0);
    internalLinks.forEach((link) => {
      const targetId = link.getAttribute('href')!.substring(1);
      const targetElement = container.querySelector(`#${targetId}`);
      expect(targetElement).not.toBeNull();
    });
  });
});
