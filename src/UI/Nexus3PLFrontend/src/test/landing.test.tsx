import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
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

  it('renders live KPI metrics ticker', () => {
    render(<App />);
    expect(screen.getByText('99.85%')).toBeDefined();
    expect(screen.getByText('1.4M+')).toBeDefined();
    expect(screen.getByText('Enterprise Facilities')).toBeDefined();
    expect(screen.getByText('< 12ms')).toBeDefined();
  });

  it('interactively updates AWB tracking simulator on preset click', () => {
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

  it('handles search input and displays error for unknown AWB', () => {
    render(<TrackingSimulator />);
    const input = screen.getByPlaceholderText(/Enter AWB or Tracking ID/i);
    const form = input.closest('form')!;

    // Type unknown AWB
    fireEvent.change(input, { target: { value: 'UNKNOWN-123' } });
    fireEvent.submit(form);

    expect(screen.getByRole('alert')).toBeDefined();
    expect(screen.getByText(/No simulated record found for "UNKNOWN-123"/i)).toBeDefined();
  });

  it('opens and closes the authentication contract notice modal', () => {
    render(<App />);
    const launchBtn = screen.getByRole('button', { name: /Open Enterprise Portal/i });
    fireEvent.click(launchBtn);

    expect(screen.getByText('Enterprise Portal Authentication Notice')).toBeDefined();
    expect(screen.getByText(/Direct Body Contract:/i)).toBeDefined();

    const closeBtn = screen.getByRole('button', { name: /Understood/i });
    fireEvent.click(closeBtn);

    expect(screen.queryByText('Enterprise Portal Authentication Notice')).toBeNull();
  });
});
