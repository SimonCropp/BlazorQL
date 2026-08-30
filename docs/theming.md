# Theming

Three modes — System, Light, Dark — cycled by the sidebar toggle or set in the settings dialog, persisted per browser. `ForcedTheme` pins the mode and hides the choice; `DefaultTheme` seeds it without pinning.

The palette is CSS custom properties on `.blazorql` using `light-dark()`, switched by a `data-theme` attribute on the document element. Override the properties to restyle:

```css
.blazorql {
    --blazorql-bg: light-dark(#ffffff, #16181d);
    --blazorql-panel: light-dark(#f6f7f9, #1d2027);
    --blazorql-border: light-dark(#d8dbe0, #2c3039);
    --blazorql-text: light-dark(#1c1e22, #d7dae0);
    --blazorql-muted: light-dark(#6a707b, #8a919d);
    --blazorql-accent: light-dark(#6c69ce, #908aff);
    --blazorql-error: light-dark(#b42318, #ff8a80);
}
```

The Monaco editors follow automatically (`blazorql-light`/`blazorql-dark` themes with a transparent editor background, so the page's own background shows through).

`light-dark()` needs a current evergreen browser — the same floor Blazor WebAssembly effectively has.
