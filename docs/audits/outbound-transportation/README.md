# Nexus3PL — Outbound, Transportation & Courier Enterprise UI Audit Evidence

This directory contains multi-viewport visual, accessibility, keyboard, and UI-state audit captures for the **Phase 7.4 Outbound, Transportation & Courier Operations Workstations**, published directly to the repository for Phase 7.4.6 consolidated review.

---

## 1. Audit Context & Environment

| Property | Value | Notes |
| :--- | :--- | :--- |
| **Branch** | `feature/phase-7-enterprise-frontend` | Phase 7.4.6 Consolidated Visual Audit |
| **Accepted Backend Baseline** | `0b804e335684570e16d31c034c53783f4dab6288` | GitHub Actions Run `45402598023` (`.NET Unit & Security` 1,375/1,375 + Live PostgreSQL CI passed) |
| **Data Mode** | **Mocked Contract Fixtures (`page.route`)** | Captured against TypeScript + Vite production preview matching WebApi DTO & enum contracts |
| **Capture Engine** | Playwright Chromium (`1440×900`, `768×1024`, `375×812`, `320×568`) | `vite build && vite preview --port 4173` |
| **Test Spec** | `src/UI/Nexus3PLFrontend/e2e/outbound-transportation-audit.spec.ts` | Combined with `outbound-orders-waves`, `outbound-picking-rf`, `outbound-packing-labels`, `outbound-shipments-bol`, `outbound-dispatch-pod`, `accessibility-audit`, and `auth-rbac` specs |

---

## 2. Targeted Accessibility, Keyboard & State Verification

- **Dialog Semantics & Focus Trapping:** `OrderDetailDrawer` (`[data-testid="order-detail-drawer"]`) and `Modal`-hosted dialogs (`ShortPickModal`, `BillOfLadingModal`, `ProofOfDeliveryModal`, `TrackingTimelineModal`) expose `role="dialog"` and `aria-modal="true"`, confine `Tab` / `Shift+Tab` keyboard traversal inside the active dialog, and close cleanly on `Escape`.
- **Keyboard Focus Visibility:** Automated `Tab` traversal confirms visible focus outlines (`outlineWidth > 0` or non-`none` `boxShadow`) on interactive buttons, tabs, and inputs.
- **Loading / Error / Retry / Empty States:** Verified explicit separation (never conflating HTTP `500` failures with empty states) with working `Retry` recovery across Outbound Orders, RF Directed Picking (`rf-error-banner` / `rf-retry-next-btn`), and Dispatch Board.
- **Zero Horizontal Overflow:** Verified `document.documentElement.scrollWidth <= window.innerWidth + 1` and `.card` / `.outbound-card` right-edge containment across `1440×900`, `768×1024`, `375×812`, and `320×568`.

---

## 3. Visual Evidence Matrix (23 Multi-Viewport Captures)

### A. Outbound Orders, Allocation & Pick Waves
| Surface | Viewport | Resolution | Screenshot |
| :--- | :--- | :---: | :--- |
| **Outbound Orders & Allocation** | Desktop | `1440 × 900` | [desktop-01-outbound-orders.png](desktop-01-outbound-orders.png) |
| **Pick Waves & Task Sequence** | Desktop | `1440 × 900` | [desktop-02-pick-waves.png](desktop-02-pick-waves.png) |
| **Outbound Orders & Allocation** | Tablet | `768 × 1024` | [tablet-01-outbound-orders.png](tablet-01-outbound-orders.png) |
| **Outbound Orders & Allocation** | Mobile | `375 × 812` | [mobile-01-outbound-orders.png](mobile-01-outbound-orders.png) |
| **Outbound Orders & Allocation** | Small Mobile | `320 × 568` | [mobile-320-01-outbound-orders.png](mobile-320-01-outbound-orders.png) |

### B. RF Directed Picking & Short-Pick Exception
| Surface | Viewport | Resolution | Screenshot |
| :--- | :--- | :---: | :--- |
| **RF Directed Picking Card** | Desktop | `1440 × 900` | [desktop-03-picking-rf.png](desktop-03-picking-rf.png) |
| **Short-Pick Exception Modal** | Desktop | `1440 × 900` | [desktop-04-short-pick-modal.png](desktop-04-short-pick-modal.png) |
| **RF Directed Picking Card** | Tablet | `768 × 1024` | [tablet-02-picking-rf.png](tablet-02-picking-rf.png) |
| **RF Directed Picking Card** | Mobile | `375 × 812` | [mobile-02-picking-rf.png](mobile-02-picking-rf.png) |
| **RF Directed Picking Card** | Small Mobile | `320 × 568` | [mobile-320-02-picking-rf.png](mobile-320-02-picking-rf.png) |

### C. Packing, 3D Cartonization & Carrier Labels
| Surface | Viewport | Resolution | Screenshot |
| :--- | :--- | :---: | :--- |
| **Packing Workbench & ZPL Label Preview** | Desktop | `1440 × 900` | [desktop-05-packing-labels.png](desktop-05-packing-labels.png) |
| **Packing Workbench & ZPL Label Preview** | Tablet | `768 × 1024` | [tablet-03-packing-labels.png](tablet-03-packing-labels.png) |
| **Packing Workbench & ZPL Label Preview** | Mobile | `375 × 812` | [mobile-03-packing-labels.png](mobile-03-packing-labels.png) |
| **Packing Workbench & ZPL Label Preview** | Small Mobile | `320 × 568` | [mobile-320-03-packing-labels.png](mobile-320-03-packing-labels.png) |

### D. Shipment Consolidation & Immutable BOL
| Surface | Viewport | Resolution | Screenshot |
| :--- | :--- | :---: | :--- |
| **Immutable Bill of Lading Snapshot Modal** | Desktop | `1440 × 900` | [desktop-06-shipments-bol.png](desktop-06-shipments-bol.png) |
| **Shipment Consolidation & Dispatch** | Tablet | `768 × 1024` | [tablet-04-shipments-bol.png](tablet-04-shipments-bol.png) |
| **Shipment Consolidation & Dispatch** | Mobile | `375 × 812` | [mobile-04-shipments-bol.png](mobile-04-shipments-bol.png) |
| **Shipment Consolidation & Dispatch** | Small Mobile | `320 × 568` | [mobile-320-04-shipments-bol.png](mobile-320-04-shipments-bol.png) |

### E. Dispatch Board, Courier Handheld & Proof of Delivery
| Surface | Viewport | Resolution | Screenshot |
| :--- | :--- | :---: | :--- |
| **Staff Dispatch & Tracking Board** | Desktop | `1440 × 900` | [desktop-07-dispatch-board.png](desktop-07-dispatch-board.png) |
| **Unsigned POD Acknowledgment Modal** | Desktop | `1440 × 900` | [desktop-08-pod-modal.png](desktop-08-pod-modal.png) |
| **Staff Dispatch & Tracking Board** | Tablet | `768 × 1024` | [tablet-05-dispatch-board.png](tablet-05-dispatch-board.png) |
| **Driver Courier Handheld** | Mobile | `375 × 812` | [mobile-05-courier-handheld.png](mobile-05-courier-handheld.png) |
| **Driver Courier Handheld** | Small Mobile | `320 × 568` | [mobile-320-05-courier-handheld.png](mobile-320-05-courier-handheld.png) |
