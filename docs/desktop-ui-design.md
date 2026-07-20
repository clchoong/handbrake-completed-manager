# Desktop UI design

The desktop interface uses a shared high-end visual system designed for clear daily use on Windows 10 and Windows 11. It remains native WPF and does not depend on a third-party UI framework.

## Design direction

- A deep navy application header establishes a stable workspace and keeps global actions predictable.
- Warm orange is reserved for the primary action, especially **Review & replace** and **Replace source safely**.
- Summary metrics are presented as individual cards with distinct color markers instead of one undifferentiated panel.
- Neutral surfaces, restrained borders, rounded geometry, and Segoe UI typography provide hierarchy without decorative clutter.
- Success, warning, and destructive actions use consistent green, amber, and red semantic treatments.
- Completed history remains the central workspace, with search and filters next to the section title and record actions grouped around the current selection.
- An original full-bleed coral, navy, and cream media/check emblem contrasts against the navy header and identifies the executable, dashboard, and notification-area icon without using HandBrake's official artwork.
- The history action bar separates the primary source-replacement workflow, everyday play/reveal actions, output recycling, and record-only removal by visual priority.

## Replacement experience

The safe replacement review is intentionally progressive:

1. The exact file plan is shown first.
2. **Replace source safely** is presented as the recommended hero action.
3. Copy and backup progress remain visible throughout the workflow.
4. Safety findings and recovery state are separated from normal status text.
5. Individual transaction controls are collapsed under **Advanced recovery controls**.

This keeps the normal path simple without removing the checkpoint-specific controls needed after interruption.

## Shared design system

`Themes/DesignSystem.xaml` defines application-wide colors, typography, cards, buttons, inputs, progress indicators, data grids, focus states, and disabled states. Main, replacement, recovery, and settings windows consume the same resources.

The source-replacement column uses plain-language durable states. **✓ Replaced** is shown only for the terminal `Completed` checkpoint, while incomplete and recovery-required work remains visibly distinct.

The interface preserves native window chrome, keyboard navigation, default/cancel behavior, readable focus indication, and Windows accessibility automation names for key search, filter, connection, and file-path controls.

## Scaling and compatibility

- The main dashboard defaults to 1400 × 900 and supports a minimum 1024-pixel width.
- At reduced height, the workspace scrolls vertically while the completed-library table retains a usable minimum height.
- Action groups wrap instead of clipping at narrower widths.
- The replacement review uses a scrollable content surface so all safety information and recovery controls remain reachable.
- No design dependency raises the existing Windows 10 build 17763 minimum target.
