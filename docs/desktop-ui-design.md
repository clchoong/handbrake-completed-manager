# Desktop UI design

The desktop interface uses a shared high-end visual system designed for clear daily use on Windows 10 and Windows 11. It remains native WPF and does not depend on a third-party UI framework.

## Design direction

- A deep navy application header establishes a stable workspace and keeps global actions predictable.
- Warm orange is reserved for the primary action, especially **Replace Source**.
- Summary metrics are presented as individual cards with distinct color markers instead of one undifferentiated panel.
- Neutral surfaces, restrained borders, rounded geometry, and Segoe UI typography provide hierarchy without decorative clutter.
- Success, warning, and destructive actions use consistent green, amber, and red semantic treatments.
- Completed history remains the central workspace, with search and filters next to the section title and record actions grouped around the current selection.
- An original full-bleed coral, navy, and cream media/check emblem contrasts against the navy header and identifies the executable, dashboard, and notification-area icon without using HandBrake's official artwork.
- The history action bar separates the primary source-replacement workflow, everyday play/reveal actions, output recycling, and record-only removal by visual priority.

## Replacement experience

The normal replacement experience is intentionally simple:

1. One warning identifies the exact paths and states that the original source cannot be recovered.
2. **Replace Source** is the default; **Replace Source and Keep Output** is secondary.
3. Copy-required operations show transferred bytes, percentage, and a Cancel action until the destructive boundary.
4. Success, cancellation, or failure remains visible until the user closes the window and can retry.

This keeps the normal path aligned with familiar cut-and-paste behavior. Legacy Recovery remains available only for unfinished operations created by earlier versions.

## Shared design system

`Themes/DesignSystem.xaml` defines application-wide colors, typography, cards, buttons, inputs, progress indicators, data grids, focus states, and disabled states. Main, replacement, recovery, settings, progress, and About windows consume the same resources.

The Status column uses plain-language file outcomes: **Output Deleted**, **Source Replaced**, and **Source Replaced, Output Kept**. A leading 1-based number follows the current filter and sort order; its selected state inherits the same blue as the other selected cells and uses white text. Header padding and the data TextBlock margin both use 16-by-6 spacing, ensuring values begin at the same inset as their titles. Output percentages from 80% to 89% use the solid Replace Source orange with white text; values at or above 90% use the equivalent red treatment regardless of row selection.

Record Details occupies a fixed 160-pixel area. Its single-record content is replaced by zero-selection or multiple-selection guidance without collapsing the area, so the table and surrounding window do not jump when selection cardinality changes.

Bulk replacement has a single resizable progress surface. It combines a continuously updated overall bar and `processed/total` counter with a byte-based bar and status for every eligible file. The overall percentage includes the active item's fractional byte progress while keeping completed, failed, cancelled, waiting, and skipped items visible together.

All secondary windows opt into WPF software rendering and invalidate their first rendered frame. This avoids transparent or blank popup surfaces seen with some Windows graphics-driver and compositor combinations.

The interface preserves native window chrome, keyboard navigation, default/cancel behavior, readable focus indication, and Windows accessibility automation names for key search, filter, connection, and file-path controls.

## Scaling and compatibility

- The main dashboard defaults to 1400 × 900 and supports a minimum 1024-pixel width.
- At reduced height, the workspace scrolls vertically while the completed-library table retains a usable minimum height.
- Action groups wrap instead of clipping at narrower widths.
- The replacement review uses a scrollable content surface so all safety information and recovery controls remain reachable.
- No design dependency raises the existing Windows 10 build 17763 minimum target.
