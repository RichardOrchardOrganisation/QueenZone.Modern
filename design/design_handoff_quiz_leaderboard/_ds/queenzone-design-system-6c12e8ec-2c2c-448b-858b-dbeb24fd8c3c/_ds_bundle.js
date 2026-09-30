/* @ds-bundle: {"format":4,"namespace":"QueenzoneDesignSystem_6c12e8","components":[{"name":"CrestSeal","sourcePath":"components/brand/CrestSeal.jsx"},{"name":"Button","sourcePath":"components/core/Button.jsx"},{"name":"IconButton","sourcePath":"components/core/IconButton.jsx"},{"name":"Input","sourcePath":"components/core/Input.jsx"},{"name":"ArticleCard","sourcePath":"components/editorial/ArticleCard.jsx"},{"name":"Badge","sourcePath":"components/editorial/Badge.jsx"},{"name":"SectionHeader","sourcePath":"components/editorial/SectionHeader.jsx"},{"name":"Tag","sourcePath":"components/editorial/Tag.jsx"},{"name":"IMAGES","sourcePath":"design_handoff_gallery/gallery-data.js"},{"name":"CATEGORIES","sourcePath":"design_handoff_gallery/gallery-data.js"},{"name":"GroupedMasthead","sourcePath":"design_handoff_grouped_masthead/GroupedMasthead.jsx"},{"name":"GROUPS","sourcePath":"design_handoff_grouped_masthead/nav-data.js"}],"sourceHashes":{"components/brand/CrestSeal.jsx":"a6e4f60a0d7a","components/core/Button.jsx":"e1c626331035","components/core/IconButton.jsx":"c1b148a846bd","components/core/Input.jsx":"aa6198645586","components/editorial/ArticleCard.jsx":"ba823b3891a9","components/editorial/Badge.jsx":"e4106eba6113","components/editorial/SectionHeader.jsx":"bd1bf5368f12","components/editorial/Tag.jsx":"9e1d0bf10f81","design_handoff_gallery/gallery-data.js":"b75176bb713e","design_handoff_grouped_masthead/GroupedMasthead.jsx":"6de9cbc0b68a","design_handoff_grouped_masthead/nav-data.js":"14fcb8584113","design_handoff_home_hero/hero-reference.jsx":"adf28357b1d4","design_handoff_timeline/tl-app.jsx":"067cffb93f5c","design_handoff_timeline/tl-data.js":"f9ae0ca9e7c3","design_handoff_timeline/tl-shared.jsx":"25da294df09d","design_handoff_timeline/tl-variant-a.jsx":"ecaeb1c93a81","explorations/timeline/tl-app.jsx":"067cffb93f5c","explorations/timeline/tl-data.js":"0ab1066b5a1b","explorations/timeline/tl-shared.jsx":"25da294df09d","explorations/timeline/tl-variant-a.jsx":"510c69a93dd4","explorations/timeline/tl-variant-b.jsx":"1dbf94ef6193","ui_kits/website/App.jsx":"3d7b6e26e72b","ui_kits/website/ArticleView.jsx":"1fcb981e4c11","ui_kits/website/Footer.jsx":"0f8f0e3bc620","ui_kits/website/Forum.jsx":"2c21c540e8b7","ui_kits/website/Header.jsx":"6e49f6f25abe","ui_kits/website/Hero.jsx":"a885bcd4d5cd","ui_kits/website/MobileScreens.jsx":"e8533243fc81","ui_kits/website/Pages1.jsx":"2ce9e74032e6","ui_kits/website/Pages2.jsx":"d3d272ec52a8","ui_kits/website/Sections1.jsx":"bd259c19b8be","ui_kits/website/Sections2.jsx":"08dbdea1af2a","ui_kits/website/data.js":"e4cfcc16482a","ui_kits/website/ios-frame.jsx":"be3343be4b51"},"inlinedExternals":[],"unexposedExports":[]} */

(() => {

const __ds_ns = (window.QueenzoneDesignSystem_6c12e8 = window.QueenzoneDesignSystem_6c12e8 || {});

const __ds_scope = {};

(__ds_ns.__errors = __ds_ns.__errors || []);

// components/brand/CrestSeal.jsx
try { (() => {
/**
 * The Queen crest as a seal / emblem / watermark — the site's visual anchor.
 * Supply `src` pointing to a crest asset (black, white, silver or line-art).
 */
function CrestSeal({
  src,
  size = 72,
  treatment = 'seal',
  alt = 'Queen crest',
  style = {}
}) {
  const treatments = {
    seal: {
      opacity: 1,
      filter: 'none'
    },
    watermark: {
      opacity: 0.06,
      filter: 'none'
    },
    ghost: {
      opacity: 0.14,
      filter: 'none'
    },
    divider: {
      opacity: 0.9,
      filter: 'none'
    }
  };
  const t = treatments[treatment] || treatments.seal;
  if (treatment === 'divider') {
    return /*#__PURE__*/React.createElement("div", {
      style: {
        display: 'flex',
        alignItems: 'center',
        gap: 'var(--space-5)',
        ...style
      }
    }, /*#__PURE__*/React.createElement("span", {
      style: {
        flex: 1,
        height: 1,
        background: 'var(--hairline)'
      }
    }), /*#__PURE__*/React.createElement("img", {
      src: src,
      alt: alt,
      style: {
        width: size,
        height: 'auto',
        opacity: 0.85
      }
    }), /*#__PURE__*/React.createElement("span", {
      style: {
        flex: 1,
        height: 1,
        background: 'var(--hairline)'
      }
    }));
  }
  return /*#__PURE__*/React.createElement("img", {
    src: src,
    alt: alt,
    style: {
      width: size,
      height: 'auto',
      opacity: t.opacity,
      filter: t.filter,
      userSelect: 'none',
      pointerEvents: 'none',
      ...style
    }
  });
}
Object.assign(__ds_scope, { CrestSeal });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/brand/CrestSeal.jsx", error: String((e && e.message) || e) }); }

// components/core/Button.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Queenzone Button — restrained, editorial. Sharp 3px corners, no gloss.
 * Variants map to the brand's sparing accent usage.
 */
function Button({
  children,
  variant = 'primary',
  size = 'md',
  iconLeft = null,
  iconRight = null,
  fullWidth = false,
  disabled = false,
  as = 'button',
  href,
  style = {},
  ...rest
}) {
  const sizes = {
    sm: {
      padding: '8px 16px',
      fontSize: '13px'
    },
    md: {
      padding: '12px 24px',
      fontSize: '14px'
    },
    lg: {
      padding: '16px 34px',
      fontSize: '15px'
    }
  };
  const variants = {
    primary: {
      background: 'var(--qz-black)',
      color: 'var(--qz-white)',
      border: '1px solid var(--qz-black)'
    },
    cta: {
      background: 'var(--qz-blue)',
      color: 'var(--qz-white)',
      border: '1px solid var(--qz-blue)'
    },
    secondary: {
      background: 'transparent',
      color: 'var(--qz-charcoal)',
      border: '1px solid var(--border-strong)'
    },
    ghost: {
      background: 'transparent',
      color: 'var(--qz-charcoal)',
      border: '1px solid transparent'
    },
    editorial: {
      background: 'var(--qz-burgundy)',
      color: 'var(--qz-white)',
      border: '1px solid var(--qz-burgundy)'
    }
  };
  const base = {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    gap: '8px',
    width: fullWidth ? '100%' : 'auto',
    fontFamily: 'var(--font-body)',
    fontWeight: 'var(--fw-medium)',
    letterSpacing: '0.04em',
    textTransform: 'uppercase',
    borderRadius: 'var(--radius-sm)',
    cursor: disabled ? 'not-allowed' : 'pointer',
    opacity: disabled ? 0.45 : 1,
    transition: 'background var(--dur-fast) var(--ease-out), color var(--dur-fast) var(--ease-out), border-color var(--dur-fast) var(--ease-out), transform var(--dur-fast) var(--ease-out)',
    textDecoration: 'none',
    whiteSpace: 'nowrap',
    ...sizes[size],
    ...variants[variant],
    ...style
  };
  const Tag = href ? 'a' : as;
  return /*#__PURE__*/React.createElement(Tag, _extends({
    href: href,
    style: base,
    disabled: disabled,
    onMouseDown: e => {
      if (!disabled) e.currentTarget.style.transform = 'translateY(1px)';
    },
    onMouseUp: e => {
      e.currentTarget.style.transform = 'translateY(0)';
    },
    onMouseLeave: e => {
      e.currentTarget.style.transform = 'translateY(0)';
    }
  }, rest), iconLeft, children, iconRight);
}
Object.assign(__ds_scope, { Button });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Button.jsx", error: String((e && e.message) || e) }); }

// components/core/IconButton.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Icon-only control — search, menu, share, bookmark. Square, quiet by default.
 */
function IconButton({
  children,
  label,
  variant = 'ghost',
  size = 'md',
  active = false,
  onDark = false,
  style = {},
  ...rest
}) {
  const dims = {
    sm: 32,
    md: 40,
    lg: 48
  }[size];
  const variants = {
    ghost: {
      background: 'transparent',
      color: onDark ? 'var(--text-on-dark)' : 'var(--qz-charcoal)',
      border: '1px solid transparent'
    },
    outline: {
      background: 'transparent',
      color: onDark ? 'var(--text-on-dark)' : 'var(--qz-charcoal)',
      border: `1px solid ${onDark ? 'var(--border-on-dark)' : 'var(--border-strong)'}`
    },
    solid: {
      background: 'var(--qz-black)',
      color: 'var(--qz-white)',
      border: '1px solid var(--qz-black)'
    }
  };
  return /*#__PURE__*/React.createElement("button", _extends({
    "aria-label": label,
    "aria-pressed": active,
    style: {
      width: dims,
      height: dims,
      display: 'inline-flex',
      alignItems: 'center',
      justifyContent: 'center',
      borderRadius: 'var(--radius-sm)',
      cursor: 'pointer',
      color: active ? 'var(--qz-blue)' : undefined,
      transition: 'background var(--dur-fast) var(--ease-out), color var(--dur-fast) var(--ease-out)',
      ...variants[variant],
      ...(active ? {
        color: 'var(--qz-blue)'
      } : {}),
      ...style
    },
    onMouseEnter: e => {
      e.currentTarget.style.background = onDark ? 'rgba(255,255,255,0.10)' : 'var(--qz-grey-100)';
    },
    onMouseLeave: e => {
      e.currentTarget.style.background = variants[variant].background;
    }
  }, rest), children);
}
Object.assign(__ds_scope, { IconButton });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/IconButton.jsx", error: String((e && e.message) || e) }); }

// components/core/Input.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Text input / search field — hairline border, generous padding, quiet focus.
 */
function Input({
  type = 'text',
  placeholder = '',
  value,
  defaultValue,
  iconLeft = null,
  size = 'md',
  invalid = false,
  disabled = false,
  fullWidth = true,
  style = {},
  onChange,
  ...rest
}) {
  const pad = {
    sm: '9px 12px',
    md: '13px 16px',
    lg: '16px 18px'
  }[size];
  const fs = {
    sm: '14px',
    md: '15px',
    lg: '16px'
  }[size];
  return /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'relative',
      width: fullWidth ? '100%' : 'auto',
      display: 'inline-flex',
      alignItems: 'center'
    }
  }, iconLeft && /*#__PURE__*/React.createElement("span", {
    style: {
      position: 'absolute',
      left: 14,
      display: 'inline-flex',
      color: 'var(--text-muted)',
      pointerEvents: 'none'
    }
  }, iconLeft), /*#__PURE__*/React.createElement("input", _extends({
    type: type,
    placeholder: placeholder,
    value: value,
    defaultValue: defaultValue,
    disabled: disabled,
    onChange: onChange,
    style: {
      width: '100%',
      padding: pad,
      paddingLeft: iconLeft ? 42 : undefined,
      font: `var(--fw-regular) ${fs}/1.4 var(--font-body)`,
      color: 'var(--text-primary)',
      background: disabled ? 'var(--qz-grey-100)' : 'var(--qz-white)',
      border: `1px solid ${invalid ? 'var(--qz-burgundy)' : 'var(--border-strong)'}`,
      borderRadius: 'var(--radius-xs)',
      outline: 'none',
      transition: 'border-color var(--dur-fast) var(--ease-out), box-shadow var(--dur-fast) var(--ease-out)',
      ...style
    },
    onFocus: e => {
      e.currentTarget.style.borderColor = invalid ? 'var(--qz-burgundy)' : 'var(--qz-blue)';
      e.currentTarget.style.boxShadow = 'var(--shadow-focus)';
    },
    onBlur: e => {
      e.currentTarget.style.borderColor = invalid ? 'var(--qz-burgundy)' : 'var(--border-strong)';
      e.currentTarget.style.boxShadow = 'none';
    }
  }, rest)));
}
Object.assign(__ds_scope, { Input });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Input.jsx", error: String((e && e.message) || e) }); }

// components/editorial/ArticleCard.jsx
try { (() => {
/**
 * Editorial story card — archival b&w image, Cormorant title, quiet meta.
 * The workhorse of the Queenzone homepage and archive grids.
 */
function ArticleCard({
  image,
  category,
  title,
  excerpt,
  meta,
  badge = null,
  href = '#',
  layout = 'vertical',
  monochrome = true,
  onDark = false,
  style = {}
}) {
  const horizontal = layout === 'horizontal';
  const titleColor = onDark ? 'var(--qz-white)' : 'var(--text-primary)';
  const excerptColor = onDark ? 'rgba(255,255,255,0.66)' : 'var(--text-secondary)';
  const metaColor = onDark ? 'rgba(255,255,255,0.5)' : 'var(--text-muted)';
  const catColor = onDark ? 'var(--qz-gold)' : 'var(--accent-archive)';
  return /*#__PURE__*/React.createElement("a", {
    href: href,
    style: {
      display: horizontal ? 'grid' : 'flex',
      gridTemplateColumns: horizontal ? '40% 1fr' : undefined,
      flexDirection: 'column',
      gap: horizontal ? 'var(--space-5)' : '0',
      background: 'transparent',
      textDecoration: 'none',
      color: 'inherit',
      ...style
    },
    className: "qz-article-card",
    onMouseEnter: e => {
      const im = e.currentTarget.querySelector('img');
      if (im) {
        im.style.transform = 'scale(1.04)';
        im.style.filter = monochrome ? 'grayscale(0)' : 'none';
      }
    },
    onMouseLeave: e => {
      const im = e.currentTarget.querySelector('img');
      if (im) {
        im.style.transform = 'scale(1)';
        im.style.filter = monochrome ? 'grayscale(1)' : 'none';
      }
    }
  }, image && /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'relative',
      overflow: 'hidden',
      borderRadius: 'var(--radius-md)',
      aspectRatio: horizontal ? '4 / 3' : '3 / 2',
      background: 'var(--qz-grey-200)'
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: image,
    alt: "",
    style: {
      width: '100%',
      height: '100%',
      objectFit: 'cover',
      filter: monochrome ? 'grayscale(1)' : 'none',
      transition: 'transform var(--dur-slow) var(--ease-out), filter var(--dur-slow) var(--ease-out)'
    }
  }), badge && /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      top: 12,
      left: 12
    }
  }, badge)), /*#__PURE__*/React.createElement("div", {
    style: {
      paddingTop: image && !horizontal ? 'var(--space-4)' : 0
    }
  }, category && /*#__PURE__*/React.createElement("div", {
    style: {
      fontFamily: 'var(--font-titling)',
      fontSize: '11px',
      fontWeight: 'var(--fw-semibold)',
      letterSpacing: 'var(--ls-eyebrow)',
      textTransform: 'uppercase',
      color: catColor,
      marginBottom: 'var(--space-2)'
    }
  }, category), /*#__PURE__*/React.createElement("h3", {
    style: {
      font: 'var(--fw-semibold) 1.5rem/1.18 var(--font-display)',
      letterSpacing: 'var(--ls-display)',
      color: titleColor,
      margin: '0 0 var(--space-2)'
    }
  }, title), excerpt && /*#__PURE__*/React.createElement("p", {
    style: {
      font: 'var(--type-body)',
      color: excerptColor,
      margin: '0 0 var(--space-3)',
      fontSize: '15px'
    }
  }, excerpt), meta && /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--type-meta)',
      color: metaColor,
      textTransform: 'uppercase',
      letterSpacing: 'var(--ls-caps)'
    }
  }, meta)));
}
Object.assign(__ds_scope, { ArticleCard });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/editorial/ArticleCard.jsx", error: String((e && e.message) || e) }); }

// components/editorial/Badge.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Editorial content marker — anniversary, premium, archive, etc.
 * Mirrors the brand's accent-by-meaning system.
 */
function Badge({
  children,
  tone = 'neutral',
  variant = 'soft',
  style = {},
  ...rest
}) {
  const tones = {
    neutral: {
      c: 'var(--qz-charcoal)',
      soft: 'var(--qz-grey-100)',
      solid: 'var(--qz-charcoal)'
    },
    archive: {
      c: 'var(--qz-purple)',
      soft: 'var(--qz-purple-tint)',
      solid: 'var(--qz-purple)'
    },
    editorial: {
      c: 'var(--qz-burgundy)',
      soft: 'var(--qz-burgundy-tint)',
      solid: 'var(--qz-burgundy)'
    },
    cta: {
      c: 'var(--qz-blue)',
      soft: 'var(--qz-blue-tint)',
      solid: 'var(--qz-blue)'
    },
    special: {
      c: 'var(--qz-gold-deep)',
      soft: 'var(--qz-gold-tint)',
      solid: 'var(--qz-gold)'
    }
  };
  const t = tones[tone] || tones.neutral;
  const looks = {
    soft: {
      background: t.soft,
      color: t.c,
      border: '1px solid transparent'
    },
    solid: {
      background: t.solid,
      color: 'var(--qz-white)',
      border: `1px solid ${t.solid}`
    },
    outline: {
      background: 'transparent',
      color: t.c,
      border: `1px solid ${t.c}`
    }
  };
  return /*#__PURE__*/React.createElement("span", _extends({
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: '6px',
      padding: '4px 10px',
      fontFamily: 'var(--font-titling)',
      fontSize: '10.5px',
      fontWeight: 'var(--fw-semibold)',
      letterSpacing: 'var(--ls-eyebrow)',
      textTransform: 'uppercase',
      borderRadius: 'var(--radius-xs)',
      lineHeight: 1,
      ...looks[variant],
      ...style
    }
  }, rest), children);
}
Object.assign(__ds_scope, { Badge });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/editorial/Badge.jsx", error: String((e && e.message) || e) }); }

// components/editorial/SectionHeader.jsx
try { (() => {
/**
 * Editorial section header — Cinzel eyebrow, Cormorant serif title, optional
 * trailing action. The primary rhythm device between homepage sections.
 */
function SectionHeader({
  eyebrow,
  title,
  action = null,
  align = 'left',
  onDark = false,
  style = {}
}) {
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'flex-end',
      justifyContent: align === 'center' ? 'center' : 'space-between',
      gap: 'var(--space-5)',
      borderBottom: align === 'center' ? 'none' : `1px solid ${onDark ? 'var(--border-on-dark)' : 'var(--hairline)'}`,
      paddingBottom: align === 'center' ? 0 : 'var(--space-4)',
      textAlign: align,
      ...style
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      width: align === 'center' ? '100%' : 'auto'
    }
  }, eyebrow && /*#__PURE__*/React.createElement("div", {
    style: {
      fontFamily: 'var(--font-titling)',
      fontSize: 'var(--fs-eyebrow)',
      fontWeight: 'var(--fw-semibold)',
      letterSpacing: 'var(--ls-eyebrow)',
      textTransform: 'uppercase',
      color: onDark ? 'var(--qz-gold)' : 'var(--accent-archive)',
      marginBottom: 'var(--space-3)'
    }
  }, eyebrow), /*#__PURE__*/React.createElement("h2", {
    style: {
      font: 'var(--type-h2)',
      color: onDark ? 'var(--text-on-dark)' : 'var(--text-primary)',
      margin: 0
    }
  }, title)), action && align !== 'center' && /*#__PURE__*/React.createElement("div", {
    style: {
      flexShrink: 0,
      paddingBottom: '4px'
    }
  }, action));
}
Object.assign(__ds_scope, { SectionHeader });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/editorial/SectionHeader.jsx", error: String((e && e.message) || e) }); }

// components/editorial/Tag.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Quiet category tag — Inter, hairline, for taxonomies (albums, eras, topics).
 * Lighter-weight than Badge; used in clusters.
 */
function Tag({
  children,
  href,
  active = false,
  onDark = false,
  style = {},
  ...rest
}) {
  const Tag_ = href ? 'a' : 'span';
  const idleText = onDark ? 'rgba(255,255,255,0.72)' : 'var(--qz-grey-700)';
  const idleBorder = onDark ? 'var(--border-on-dark)' : 'var(--border-strong)';
  const hoverText = onDark ? 'var(--qz-white)' : 'var(--qz-charcoal)';
  const hoverBorder = onDark ? 'rgba(255,255,255,0.5)' : 'var(--qz-charcoal)';
  const activeBg = onDark ? 'var(--qz-white)' : 'var(--qz-charcoal)';
  const activeText = onDark ? 'var(--qz-charcoal)' : 'var(--qz-white)';
  return /*#__PURE__*/React.createElement(Tag_, _extends({
    href: href,
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      padding: '5px 12px',
      font: `var(--fw-medium) 12px/1 var(--font-body)`,
      letterSpacing: '0.04em',
      color: active ? activeText : idleText,
      background: active ? activeBg : 'transparent',
      border: `1px solid ${active ? activeBg : idleBorder}`,
      borderRadius: 'var(--radius-pill)',
      cursor: href ? 'pointer' : 'default',
      transition: 'all var(--dur-fast) var(--ease-out)',
      textDecoration: 'none',
      ...style
    },
    onMouseEnter: e => {
      if (!active && href) {
        e.currentTarget.style.borderColor = hoverBorder;
        e.currentTarget.style.color = hoverText;
      }
    },
    onMouseLeave: e => {
      if (!active && href) {
        e.currentTarget.style.borderColor = idleBorder;
        e.currentTarget.style.color = idleText;
      }
    }
  }, rest), children);
}
Object.assign(__ds_scope, { Tag });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/editorial/Tag.jsx", error: String((e && e.message) || e) }); }

// design_handoff_gallery/gallery-data.js
try { (() => {
// Gallery (Photography) — content data.
// Edit THIS file to add/move/rename collections and images.
//
// In the prototype three restored sleeves stand in for the whole archive,
// keyed a/b/c. In production replace IMAGES with real per-image sources
// (and ideally a separate small thumbSrc) — one entry per photograph.
//
// Each shot row is [imgKey, title, meta, caption]:
//   imgKey  — key into IMAGES (prototype) → real src in production
//   title   — shown under the thumbnail and as the lightbox heading
//   meta    — gold uppercase line (year · format)
//   caption — lightbox-only description
//
// Page count, thumbnail numbering and the "Showing 1–12 of N" range label
// are all derived from shots.length + the perPage prop — never hand-set.

const IMAGES = {
  a: '/assets/gallery/innuendo.jpg',
  b: '/assets/gallery/greatest-hits-iii.jpg',
  c: '/assets/gallery/hot-space.jpg'
};
const CATEGORIES = [{
  name: 'Album Artwork',
  blurb: 'Original sleeves, inner gatefolds and single covers, scanned front and back.',
  cover: 'a',
  shots: [['a', 'Innuendo', '1991 · LP sleeve', 'The defiant penultimate album cover, photographed for the 1991 Parlophone release.'], ['b', 'Greatest Hits III', '1999 · Compilation', 'The third hits collection gathering the solo and late-period material.'], ['c', 'Hot Space', '1982 · Gatefold', 'The four-panel grid sleeve for the band’s divisive funk-and-dance turn.'], ['a', 'A Night at the Opera', '1975 · Crest sleeve', 'The crest-emblazoned cover of the band’s operatic masterpiece.'], ['b', 'News of the World', '1977 · Front', 'Frank Kelly Freas’ robot illustration, restored from an original pressing.'], ['c', 'Jazz', '1978 · Inner', 'The eclectic 1978 record’s inner sleeve artwork.'], ['a', 'The Game', '1980 · Front', 'The monochrome band portrait that fronted their US breakthrough.'], ['b', 'The Works', '1984 · Sleeve', 'The comeback album sleeve, scanned at high resolution.'], ['c', 'A Kind of Magic', '1986 · Front', 'The illustrated cover tied to the Highlander era.'], ['a', 'Made in Heaven', '1995 · Front', 'The posthumous final album’s cover, shot at Montreux.'], ['b', 'The Miracle', '1989 · Morphed faces', 'The composited band portrait of the unified 1989 record.'], ['c', 'Sheer Heart Attack', '1974 · Front', 'The oiled-and-prone band shot from the breakthrough album.'], ['a', 'Queen II', '1974 · Black side', 'The iconic Mick Rock diamond-pose photograph.'], ['b', 'Innuendo', '1991 · Back sleeve', 'The reverse of the Innuendo sleeve with tracklisting.'], ['c', 'A Day at the Races', '1976 · Front', 'The companion piece to Opera, in its plain crest sleeve.'], ['a', 'Flash Gordon', '1980 · Soundtrack', 'The film-tie-in cover for the cult sci-fi score.'], ['b', 'Greatest Hits', '1981 · Compilation', 'The best-selling UK album of all time, in its original sleeve.'], ['c', 'Hot Space', '1982 · Back', 'The reverse grid of the Hot Space sleeve.']]
}, {
  name: 'Live & Stadium',
  blurb: 'On stage from the club years to Wembley, Knebworth and Live Aid.',
  cover: 'b',
  shots: [['b', 'Live Aid, Wembley', '1985 · 35mm', 'Freddie at the piano during the celebrated twenty-one minute set.'], ['c', 'Magic Tour, Knebworth', '1986 · Stage', 'The final live show before an enormous open-air crowd.'], ['a', 'Hyde Park', '1976 · Free concert', 'The free London concert that drew a vast summer crowd.'], ['b', 'Hammersmith Odeon', '1979 · Crowd', 'Captured on the Crazy tour of British theatres.'], ['c', 'Earls Court', '1977 · Crown rig', 'The famous crown-shaped lighting rig in full effect.'], ['a', 'Montreal', '1981 · We Will Rock You', 'From the concert filmed for the live release.'], ['b', 'Milton Keynes Bowl', '1982 · Daylight', 'A rare daylight stadium show from the Hot Space tour.'], ['c', 'Sun City', '1984 · The Works tour', 'From the controversial Bophuthatswana residency.'], ['a', 'Wembley Stadium', '1986 · Two nights', 'Immortalised on film and record on the Magic Tour.'], ['b', 'Rock in Rio', '1985 · Brazil', 'Before one of the largest crowds the band ever played.'], ['c', 'Budapest', '1986 · Népstadion', 'The landmark show behind the Iron Curtain.'], ['a', 'Frankfurt', '1984 · Festhalle', 'From the European leg of The Works tour.'], ['b', 'Tokyo', '1975 · Budokan', 'Early scenes of Japanese Queen-mania.'], ['c', 'Live Killers era', '1979 · Composite', 'A montage frame from the live-album sessions.']]
}, {
  name: 'Studio Sessions',
  blurb: 'At the desk and behind the glass, from Trident to Mountain.',
  cover: 'c',
  shots: [['c', 'Trident Studios', '1973 · Night sessions', 'Where the debut was cut in stolen night-time hours.'], ['a', 'Rockfield', '1975 · Bohemian Rhapsody', 'During the marathon overdub sessions in Wales.'], ['b', 'Mountain Studios', '1979 · Montreux', 'The Montreux room the band eventually bought.'], ['c', 'Musicland', '1980 · Munich', 'Where much of the early-eighties material took shape.'], ['a', 'The Townhouse', '1982 · Mixing', 'Late-night mixing of the Hot Space material.'], ['b', 'Sarm West', '1984 · The Works', 'Brian and Freddie at the console.'], ['c', 'Olympic Studios', '1989 · The Miracle', 'The unified sessions of the late period.'], ['a', 'Metropolis', '1991 · Innuendo', 'The final full-band recordings.']]
}, {
  name: 'Backstage & Candid',
  blurb: 'Dressing rooms, tour buses and quiet moments off stage.',
  cover: 'a',
  shots: [['a', 'Dressing room', '1977 · Polaroid', 'A candid moment before a News of the World show.'], ['b', 'On the tour bus', '1978 · Jazz tour', 'Between dates on the North American run.'], ['c', 'Soundcheck', '1980 · The Game', 'An empty-arena afternoon soundcheck.'], ['a', 'Backstage, Wembley', '1986 · Magic Tour', 'Minutes before walking on stage.'], ['b', 'Rehearsal room', '1984 · The Works', 'Working up the live arrangements.'], ['c', 'Airport', '1976 · Japan', 'Arriving to crowds of fans in Tokyo.'], ['a', 'Hotel suite', '1982 · Hot Space', 'A quiet day off on the European tour.'], ['b', 'Catering', '1985 · Rock in Rio', 'A lighter moment behind the scenes.'], ['c', 'With the crew', '1986 · Knebworth', 'The band and road crew on the final night.'], ['a', 'Press junket', '1989 · The Miracle', 'Promotion duties for the comeback record.']]
}, {
  name: 'Press & Magazines',
  blurb: 'Covers, cuttings and interviews from the music press.',
  cover: 'b',
  shots: [['b', 'Melody Maker', '1974 · Cover', 'An early feature as the band broke through.'], ['c', 'NME', '1975 · Bohemian Rhapsody', 'Coverage of the era-defining single.'], ['a', 'Rolling Stone', '1977 · Feature', 'The American press takes notice.'], ['b', 'Smash Hits', '1980 · Poster', 'A pull-out from the height of their fame.'], ['c', 'Record Mirror', '1982 · Interview', 'Discussing the Hot Space direction.'], ['a', 'Q Magazine', '1991 · Retrospective', 'A career retrospective from the final year.']]
}, {
  name: 'Memorabilia',
  blurb: 'Tickets, tour programmes, badges and pressed vinyl.',
  cover: 'c',
  shots: [['c', 'Tour programme', '1977 · A World tour', 'The glossy book sold on the News of the World run.'], ['a', 'Concert ticket', '1986 · Wembley', 'A stub from the final UK stadium shows.'], ['b', 'Promo badge', '1980 · The Game', 'An EMI promotional pin badge.'], ['c', 'Picture disc', '1978 · Bicycle Race', 'A collectable shaped picture-disc pressing.'], ['a', 'Backstage pass', '1984 · The Works', 'A laminated all-areas tour pass.'], ['b', 'Fan-club flyer', '1975 · Official', 'An original membership mailing.']]
}];
Object.assign(__ds_scope, { IMAGES, CATEGORIES });
})(); } catch (e) { __ds_ns.__errors.push({ path: "design_handoff_gallery/gallery-data.js", error: String((e && e.message) || e) }); }

// design_handoff_grouped_masthead/nav-data.js
try { (() => {
// Grouped Masthead — information architecture.
// Three top-level groups; new sections slot into an existing group so the
// nav bar never grows. Edit THIS file to add/move/rename a section.
//
// accent: brand token used for the group's eyebrow label + hover bar.
// items[].tag: optional pill (e.g. 'New'); rendered in antique gold.
// items[].href: real route — replace the '#' placeholders on integration.

const GROUPS = [{
  label: 'The Band',
  eyebrow: 'About Queen',
  accent: 'var(--qz-purple)',
  items: [{
    title: 'Biography',
    href: '#',
    desc: 'The story of the band, member by member',
    tag: 'New'
  }, {
    title: 'Discography',
    href: '#',
    desc: 'Every studio album, single and release',
    tag: 'New'
  }, {
    title: 'Timeline',
    href: '#',
    desc: 'Five decades, year by year'
  }]
}, {
  label: 'Archive',
  eyebrow: 'The Publication',
  accent: 'var(--qz-blue)',
  items: [{
    title: 'News',
    href: '#',
    desc: '4,000+ articles from the original archive'
  }, {
    title: 'Stories',
    href: '#',
    desc: 'Long-form features and editorial'
  }, {
    title: 'Photography',
    href: '#',
    desc: 'Tens of thousands of restored images'
  }]
}, {
  label: 'Community',
  eyebrow: 'The Fans',
  accent: 'var(--qz-burgundy)',
  items: [{
    title: 'Forum',
    href: '#',
    desc: '100,000+ posts from the membership'
  }, {
    title: 'Fan Performances',
    href: '#',
    desc: 'Covers, tributes and live sets',
    tag: 'New'
  }]
}];
Object.assign(__ds_scope, { GROUPS });
})(); } catch (e) { __ds_ns.__errors.push({ path: "design_handoff_grouped_masthead/nav-data.js", error: String((e && e.message) || e) }); }

// design_handoff_grouped_masthead/GroupedMasthead.jsx
try { (() => {
// Grouped Masthead — reference implementation (desktop).
// Cleaned from the prototype in ui_kits/website/grouped-masthead.html:
// the demo-only hero/light bands and dark/light toggle are removed.
//
// Wire `dark` from the route (true on pages with a black hero, false elsewhere)
// and `scrolled` from your scroll listener. See README.md §3–§4 for full spec,
// and §4 for the accessibility work this reference does NOT yet implement.
//
// Components come from the Queenzone design-system bundle.
// Data comes from ./nav-data.js (GROUPS).

const {
  useState,
  useRef
} = React;
const {
  IconButton,
  Button
} = window.QueenzoneDesignSystem_6c12e8;
function Chevron({
  open,
  color
}) {
  return /*#__PURE__*/React.createElement("svg", {
    width: "11",
    height: "11",
    viewBox: "0 0 24 24",
    fill: "none",
    stroke: color,
    strokeWidth: "2",
    strokeLinecap: "round",
    strokeLinejoin: "round",
    style: {
      transition: 'transform var(--dur-fast) var(--ease-out)',
      transform: open ? 'rotate(180deg)' : 'none'
    }
  }, /*#__PURE__*/React.createElement("path", {
    d: "m6 9 6 6 6-6"
  }));
}
function Panel({
  group,
  dark
}) {
  const surface = dark ? '#171717' : 'var(--qz-white)';
  const border = dark ? 'rgba(255,255,255,0.12)' : 'var(--qz-grey-200)';
  const titleC = dark ? 'var(--qz-white)' : 'var(--qz-charcoal)';
  const descC = dark ? 'rgba(255,255,255,0.55)' : 'var(--qz-grey-500)';
  const hover = dark ? 'rgba(255,255,255,0.05)' : 'var(--qz-warm-white)';
  const [hi, setHi] = useState(-1);
  return /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      top: 'calc(100% + 14px)',
      left: -16,
      minWidth: 320,
      background: surface,
      border: `1px solid ${border}`,
      borderRadius: 4,
      boxShadow: dark ? '0 24px 60px rgba(0,0,0,0.55)' : '0 18px 50px rgba(17,17,17,0.14)',
      padding: '18px 14px 14px',
      animation: 'qzPanel var(--dur-fast) var(--ease-out)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      font: '600 10px/1 var(--font-titling)',
      letterSpacing: '0.22em',
      textTransform: 'uppercase',
      color: group.accent,
      padding: '0 12px 12px',
      marginBottom: 4,
      borderBottom: `1px solid ${border}`
    }
  }, group.eyebrow), group.items.map((it, i) => /*#__PURE__*/React.createElement("a", {
    key: it.title,
    href: it.href,
    onMouseEnter: () => setHi(i),
    onMouseLeave: () => setHi(-1),
    style: {
      display: 'block',
      textDecoration: 'none',
      padding: '11px 12px',
      borderRadius: 3,
      background: hi === i ? hover : 'transparent',
      position: 'relative',
      transition: 'background var(--dur-fast) var(--ease-out)'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      position: 'absolute',
      left: 0,
      top: 14,
      bottom: 14,
      width: 2,
      borderRadius: 2,
      background: group.accent,
      opacity: hi === i ? 1 : 0,
      transition: 'opacity var(--dur-fast) var(--ease-out)'
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 9
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      font: '500 15px/1.2 var(--font-body)',
      color: titleC
    }
  }, it.title), it.tag && /*#__PURE__*/React.createElement("span", {
    style: {
      font: '600 9px/1 var(--font-titling)',
      letterSpacing: '0.14em',
      textTransform: 'uppercase',
      color: 'var(--qz-gold)',
      border: '1px solid rgba(184,154,74,0.5)',
      borderRadius: 2,
      padding: '3px 5px 2px'
    }
  }, it.tag)), /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'block',
      font: '400 13px/1.4 var(--font-body)',
      color: descC,
      marginTop: 3
    }
  }, it.desc))));
}

// dark: invert for a black-hero page.  scrolled: pass true past ~12px scroll.
function GroupedMasthead({
  dark = false,
  scrolled = false
}) {
  const [open, setOpen] = useState(-1);
  const timer = useRef(null);
  const enter = i => {
    clearTimeout(timer.current);
    setOpen(i);
  };
  const leave = () => {
    timer.current = setTimeout(() => setOpen(-1), 130);
  };
  const bg = dark ? scrolled ? 'rgba(17,17,17,0.92)' : 'var(--qz-black)' : scrolled ? 'rgba(255,255,255,0.92)' : 'var(--qz-white)';
  const wordmark = dark ? 'var(--qz-white)' : 'var(--qz-charcoal)';
  const navIdle = dark ? 'rgba(255,255,255,0.82)' : 'var(--qz-charcoal)';
  const navActive = dark ? 'var(--qz-gold)' : 'var(--qz-blue)';
  const crest = dark ? '/assets/crest-white.png' : '/assets/crest-black.png';
  return /*#__PURE__*/React.createElement("header", {
    style: {
      position: 'sticky',
      top: 0,
      zIndex: 50,
      background: bg,
      backdropFilter: scrolled ? 'saturate(180%) blur(12px)' : 'none',
      borderBottom: '1px solid rgba(184,154,74,0.55)',
      boxShadow: scrolled ? dark ? '0 8px 28px rgba(0,0,0,0.45)' : '0 6px 22px rgba(17,17,17,0.10)' : 'none',
      transition: 'background var(--dur-base) var(--ease-out), box-shadow var(--dur-base) var(--ease-out)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 'var(--container-max)',
      margin: '0 auto',
      padding: '0 var(--gutter-lg)',
      height: 76,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: 24
    }
  }, /*#__PURE__*/React.createElement("a", {
    href: "/",
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 14,
      textDecoration: 'none'
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: crest,
    alt: "Queen crest",
    style: {
      height: 42,
      width: 'auto'
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: 'var(--font-titling)',
      fontWeight: 600,
      fontSize: 21,
      letterSpacing: '0.18em',
      textTransform: 'uppercase',
      color: wordmark
    }
  }, "Queenzone")), /*#__PURE__*/React.createElement("nav", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 8
    }
  }, __ds_scope.GROUPS.map((g, i) => /*#__PURE__*/React.createElement("div", {
    key: g.label,
    style: {
      position: 'relative'
    },
    onMouseEnter: () => enter(i),
    onMouseLeave: leave
  }, /*#__PURE__*/React.createElement("button", {
    onClick: () => setOpen(open === i ? -1 : i),
    "aria-haspopup": "true",
    "aria-expanded": open === i,
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 6,
      cursor: 'pointer',
      background: 'none',
      border: 'none',
      padding: '8px 14px',
      font: '500 14px/1 var(--font-body)',
      letterSpacing: '0.03em',
      color: open === i ? navActive : navIdle,
      transition: 'color var(--dur-fast) var(--ease-out)'
    }
  }, g.label, /*#__PURE__*/React.createElement(Chevron, {
    open: open === i,
    color: open === i ? navActive : navIdle
  })), open === i && /*#__PURE__*/React.createElement(Panel, {
    group: g,
    dark: dark
  })))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 10
    }
  }, /*#__PURE__*/React.createElement(IconButton, {
    label: "Search",
    variant: "ghost",
    onDark: dark
  }, /*#__PURE__*/React.createElement("i", {
    "data-lucide": "search",
    style: {
      width: 19,
      height: 19
    }
  })), /*#__PURE__*/React.createElement(Button, {
    variant: "secondary",
    size: "sm",
    style: dark ? {
      color: 'var(--qz-white)',
      borderColor: 'var(--border-on-dark)'
    } : {}
  }, "Sign in"))));
}
Object.assign(__ds_scope, { GroupedMasthead });
})(); } catch (e) { __ds_ns.__errors.push({ path: "design_handoff_grouped_masthead/GroupedMasthead.jsx", error: String((e && e.message) || e) }); }

// design_handoff_home_hero/hero-reference.jsx
try { (() => {
// Homepage hero — a living archive: the previous eras of Queenzone.com
// morph across a floating "site window", reinforcing that this is the
// restored home of two decades of Queen community history.
const QZ_ERAS = [{
  year: '1999',
  img: '../../assets/eras/queenzone-1999.png',
  label: 'The Queen Internet Zone',
  glow: '#c81e2e'
}, {
  year: '2000',
  img: '../../assets/eras/queenzone-2000.png',
  label: 'Queen Internet Zone',
  glow: '#3c4a5a'
}, {
  year: '2002',
  img: '../../assets/eras/queenzone-2002.png',
  label: 'www.queenzone.com',
  glow: '#9c1414'
}, {
  year: '2004',
  img: '../../assets/eras/queenzone-2004.png',
  label: 'Queenzone.com',
  glow: '#1668ad'
}, {
  year: '2020',
  img: '../../assets/eras/queenzone-2020.png',
  label: 'QUEENZONE.COM',
  glow: '#8b95a1'
}];

// NOTE: this reference file is named `hero-reference.jsx` so it doesn't collide
// with the design system's live Hero during compilation. In your app, save it
// as `Hero.jsx` and register/export it however your build expects, e.g.:
//   window.Hero = Hero;   // for <script>-loaded UI kits
//   export { Hero };      // for module builds
function Hero({
  onOpen,
  onExplore
}) {
  const {
    Button,
    Badge
  } = window.QueenzoneDesignSystem_6c12e8;
  const h = window.QZ_DATA.hero;
  const [i, setI] = React.useState(0);
  React.useEffect(() => {
    const t = setInterval(() => setI(n => (n + 1) % QZ_ERAS.length), 3600);
    return () => clearInterval(t);
  }, []);
  const active = QZ_ERAS[i];
  return /*#__PURE__*/React.createElement("section", {
    style: {
      position: 'relative',
      minHeight: 'min(84vh, 760px)',
      display: 'flex',
      alignItems: 'center',
      overflow: 'hidden',
      background: 'var(--qz-black)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      top: '-10%',
      right: '-6%',
      width: '70%',
      height: '120%',
      pointerEvents: 'none',
      background: 'radial-gradient(closest-side, ' + active.glow + '55, transparent 72%)',
      transition: 'background 900ms var(--ease-out)',
      filter: 'blur(8px)'
    }
  }), /*#__PURE__*/React.createElement("img", {
    src: "../../assets/crest-white.png",
    alt: "",
    style: {
      position: 'absolute',
      top: 40,
      right: 48,
      width: 120,
      opacity: 0.08,
      pointerEvents: 'none'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'relative',
      width: '100%',
      maxWidth: 'var(--container-max)',
      margin: '0 auto',
      padding: '0 var(--gutter-lg)',
      display: 'grid',
      gridTemplateColumns: 'minmax(0, 1fr) minmax(0, 1.05fr)',
      gap: 'clamp(32px, 5vw, 80px)',
      alignItems: 'center'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 560
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      marginBottom: 22
    }
  }, /*#__PURE__*/React.createElement(Badge, {
    tone: "editorial",
    variant: "solid"
  }, h.category)), /*#__PURE__*/React.createElement("h1", {
    style: {
      font: 'var(--fw-medium) clamp(40px, 5vw, 68px)/1.03 var(--font-display)',
      letterSpacing: '-0.015em',
      color: 'var(--qz-white)',
      margin: '0 0 22px'
    }
  }, h.title), /*#__PURE__*/React.createElement("p", {
    style: {
      font: 'var(--fw-regular) 19px/1.55 var(--font-body)',
      color: 'rgba(255,255,255,0.82)',
      margin: '0 0 30px',
      maxWidth: 500
    }
  }, h.standfirst), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 22,
      flexWrap: 'wrap'
    }
  }, /*#__PURE__*/React.createElement(Button, {
    variant: "cta",
    size: "lg",
    onClick: onExplore || onOpen
  }, "Explore the timeline"), /*#__PURE__*/React.createElement("span", {
    style: {
      font: 'var(--fw-medium) 13px/1 var(--font-body)',
      textTransform: 'uppercase',
      letterSpacing: '0.08em',
      color: 'rgba(255,255,255,0.55)'
    }
  }, h.meta))), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'relative',
      width: '100%',
      aspectRatio: '4 / 3',
      maxHeight: 520
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      inset: '18px -22px -20px 26px',
      border: '1px solid rgba(255,255,255,0.09)',
      borderRadius: 10,
      transform: 'rotate(2.2deg)',
      background: 'rgba(255,255,255,0.02)'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      inset: '9px -11px -10px 13px',
      border: '1px solid rgba(255,255,255,0.12)',
      borderRadius: 10,
      transform: 'rotate(1deg)',
      background: 'rgba(255,255,255,0.03)'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      inset: 0,
      borderRadius: 10,
      overflow: 'hidden',
      border: '1px solid rgba(184,154,74,0.5)',
      boxShadow: '0 40px 90px rgba(0,0,0,0.6)',
      background: '#0d0d0d'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'relative',
      zIndex: 3,
      height: 38,
      display: 'flex',
      alignItems: 'center',
      gap: 8,
      padding: '0 14px',
      background: '#171717',
      borderBottom: '1px solid rgba(184,154,74,0.35)'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      width: 9,
      height: 9,
      borderRadius: '50%',
      background: '#3a3a3a'
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      width: 9,
      height: 9,
      borderRadius: '50%',
      background: '#3a3a3a'
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      width: 9,
      height: 9,
      borderRadius: '50%',
      background: '#3a3a3a'
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      marginLeft: 10,
      font: 'var(--fw-semibold) 11px/1 var(--font-titling)',
      letterSpacing: '0.14em',
      textTransform: 'uppercase',
      color: 'rgba(255,255,255,0.6)',
      transition: 'color 500ms'
    }
  }, active.label), /*#__PURE__*/React.createElement("span", {
    style: {
      marginLeft: 'auto',
      font: 'var(--fw-medium) 12px/1 var(--font-titling)',
      letterSpacing: '0.12em',
      color: 'var(--qz-gold)'
    }
  }, active.year)), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      top: 38,
      left: 0,
      right: 0,
      bottom: 0,
      overflow: 'hidden'
    }
  }, /*#__PURE__*/React.createElement("img", {
    key: active.year,
    src: active.img,
    alt: 'Queenzone.com in ' + active.year,
    style: {
      position: 'absolute',
      inset: 0,
      width: '100%',
      height: '100%',
      objectFit: 'cover',
      objectPosition: 'top center',
      opacity: 1
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      inset: 0,
      pointerEvents: 'none',
      background: 'linear-gradient(180deg, transparent 55%, rgba(13,13,13,0.35))',
      backgroundImage: 'repeating-linear-gradient(0deg, rgba(0,0,0,0.14) 0 1px, transparent 1px 3px)'
    }
  }))), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      left: 0,
      bottom: -46,
      display: 'flex',
      alignItems: 'center',
      gap: 16
    }
  }, QZ_ERAS.map((e, n) => /*#__PURE__*/React.createElement("button", {
    key: e.year,
    onClick: () => setI(n),
    style: {
      background: 'none',
      border: 'none',
      cursor: 'pointer',
      padding: '4px 0',
      font: 'var(--fw-medium) 12px/1 var(--font-titling)',
      letterSpacing: '0.14em',
      color: n === i ? 'var(--qz-gold)' : 'rgba(255,255,255,0.4)',
      transition: 'color 300ms',
      position: 'relative'
    }
  }, e.year, /*#__PURE__*/React.createElement("span", {
    style: {
      position: 'absolute',
      left: 0,
      right: 0,
      bottom: -6,
      height: 2,
      background: 'var(--qz-gold)',
      borderRadius: 2,
      opacity: n === i ? 1 : 0,
      transition: 'opacity 300ms'
    }
  })))))));
}
// ── Handoff reference copy. The live component lives at ui_kits/website/Hero.jsx.
// In your app, register/export it however your build expects, e.g.:
//   window.Hero = Hero;            // for <script>-loaded UI kits
//   export { Hero };               // for module builds
// (Left unassigned here so this reference copy doesn't collide with the
//  design system's live Hero during compilation.)
})(); } catch (e) { __ds_ns.__errors.push({ path: "design_handoff_home_hero/hero-reference.jsx", error: String((e && e.message) || e) }); }

// design_handoff_timeline/tl-app.jsx
try { (() => {
// App shell — mounts the Decades timeline (the direction taken forward for scale).
ReactDOM.createRoot(document.getElementById('root')).render(/*#__PURE__*/React.createElement(TimelineDecades, null));
})(); } catch (e) { __ds_ns.__errors.push({ path: "design_handoff_timeline/tl-app.jsx", error: String((e && e.message) || e) }); }

// design_handoff_timeline/tl-data.js
try { (() => {
// Queenzone Timeline — researched event data (1970 → today).
// PLEASE VERIFY the facts/dates; the final 2024 entry especially (catalogue deal).
// Category → brand accent:
//   music     Royal Blue     releases & recordings
//   live      Royal Purple   tours & landmark performances
//   milestone Antique Gold   formation, honours, cultural landmarks (rarest accent)
//   loss      Burgundy       Freddie's passing & farewell
// NOTE: the questionnaire listed a "Community" category — I folded the two
// community/fan moments (Tribute Concert, Queenzone founding) into "milestone"
// to keep the palette to four accents. Say the word and I'll split it back out.

window.QZ_CATS = {
  music: {
    label: 'Music',
    color: 'var(--qz-blue)',
    tint: 'var(--qz-blue-tint)',
    deep: 'var(--qz-blue-deep)'
  },
  live: {
    label: 'Live',
    color: 'var(--qz-purple)',
    tint: 'var(--qz-purple-tint)',
    deep: 'var(--qz-purple-deep)'
  },
  milestone: {
    label: 'Milestone',
    color: 'var(--qz-gold-deep)',
    tint: 'var(--qz-gold-tint)',
    deep: 'var(--qz-gold-deep)'
  },
  loss: {
    label: 'Loss',
    color: 'var(--qz-burgundy)',
    tint: 'var(--qz-burgundy-tint)',
    deep: 'var(--qz-burgundy-deep)'
  }
};

// img: a real asset path (greyscaled in-page) or null for an archival placeholder.
window.QZ_TIMELINE = [{
  year: '1970',
  cat: 'milestone',
  title: 'Queen is born',
  text: 'Freddie Bulsara joins Brian May and Roger Taylor\u2019s band Smile, renames it Queen and designs the regal crest that still anchors everything.',
  more: 'The first billed performance as Queen takes place in 1970; Freddie adopts the surname Mercury soon after.',
  img: null
}, {
  year: '1971',
  cat: 'milestone',
  title: 'The classic line-up',
  text: 'John Deacon joins on bass, completing the four-piece that would stay intact for twenty years.',
  more: 'Deacon was the last of some sixty bassists the band auditioned \u2014 chosen as much for his quiet steadiness as his playing.',
  img: null
}, {
  year: '1973',
  cat: 'music',
  title: 'The debut album',
  text: 'Queen release their self-titled debut on 13 July 1973, recorded largely in stolen night-time hours at Trident Studios.',
  more: 'Much of it was cut using downtime the band were given in exchange for being \u201Cguinea pigs\u201D for new studio equipment.',
  img: null
}, {
  year: '1974',
  cat: 'music',
  title: 'Killer Queen',
  text: '\u2018Sheer Heart Attack\u2019 and the \u2018Killer Queen\u2019 single deliver the band\u2019s first major chart success on both sides of the Atlantic.',
  more: 'Written by Freddie, \u2018Killer Queen\u2019 reached No. 2 in the UK and announced the band\u2019s theatrical ambition.',
  img: null
}, {
  year: '1975',
  cat: 'music',
  title: 'A Night at the Opera',
  text: 'The lavish album \u2014 and \u2018Bohemian Rhapsody\u2019, UK No. 1 for nine weeks \u2014 rewrite the rules of the rock single.',
  more: 'Reputedly the most expensive album ever made to that point; its promo film is often credited as pop\u2019s first true music video.',
  img: null
}, {
  year: '1977',
  cat: 'music',
  title: 'We Will Rock You',
  text: '\u2018News of the World\u2019 gives the world two of the most-played stadium anthems ever written, back to back on side one.',
  more: '\u2018We Will Rock You\u2019 and \u2018We Are the Champions\u2019 were designed for crowds to sing \u2014 and have been sung ever since.',
  img: null
}, {
  year: '1980',
  cat: 'music',
  title: 'The Game',
  text: 'Their first US No. 1 album, powered by \u2018Crazy Little Thing Called Love\u2019 and \u2018Another One Bites the Dust\u2019.',
  more: 'The first Queen album to use a synthesiser \u2014 a deliberate break from the \u201Cno synths\u201D note printed on earlier sleeves.',
  img: null
}, {
  year: '1981',
  cat: 'music',
  title: 'Greatest Hits',
  text: 'The compilation becomes the best-selling album in British history; the band play vast South American stadiums.',
  more: 'It has since sold many millions of copies and is a fixture of \u201Cbest-selling albums of all time\u201D lists.',
  img: null
}, {
  year: '1985',
  cat: 'live',
  title: 'Live Aid',
  text: 'On 13 July at Wembley, twenty-one minutes widely regarded as the greatest live performance in rock.',
  more: 'The set was tightly rehearsed and played to the back row; it revived the band\u2019s fortunes overnight.',
  img: 'assets/img-hero.jpg'
}, {
  year: '1986',
  cat: 'live',
  title: 'The Magic Tour',
  text: 'The final tour with all four members draws record crowds; the last show with Freddie takes place at Knebworth on 9 August.',
  more: 'No one knew at the time that Knebworth would be the last time the classic line-up played live together.',
  img: 'assets/img-crowd.jpg'
}, {
  year: '1991',
  cat: 'loss',
  title: 'Innuendo & farewell',
  text: 'The \u2018Innuendo\u2019 album tops the UK chart. On 24 November, Freddie Mercury dies, a day after confirming his illness.',
  more: '\u2018The Show Must Go On\u2019 \u2014 recorded when he could barely stand \u2014 became a defiant epitaph.',
  img: 'assets/img-portrait.jpg'
}, {
  year: '1992',
  cat: 'milestone',
  title: 'The Tribute Concert',
  text: 'On 20 April, 72,000 fill Wembley for The Freddie Mercury Tribute Concert, raising awareness and funds for AIDS.',
  more: 'Broadcast worldwide to an estimated billion viewers, it launched the Mercury Phoenix Trust.',
  img: null
}, {
  year: '1995',
  cat: 'music',
  title: 'Made in Heaven',
  text: 'The posthumous final album, built around Freddie\u2019s last vocals, is released in November.',
  more: 'The surviving members completed the recordings from sessions Freddie insisted on making while he still could.',
  img: null
}, {
  year: '1999',
  cat: 'milestone',
  title: 'Queenzone.com',
  text: 'The online fan community that would become this archive is founded \u2014 the beginning of two decades of gathering.',
  more: 'From hand-coded HTML to a 100,000-post forum, Queenzone became one of the definitive fan homes on the web.',
  img: null
}, {
  year: '2001',
  cat: 'milestone',
  title: 'Rock and Roll Hall of Fame',
  text: 'Queen are inducted, formally recognising their place in the history of popular music.',
  more: 'A run of honours followed through the 2000s, including songwriting and industry lifetime awards.',
  img: null
}, {
  year: '2002',
  cat: 'milestone',
  title: 'We Will Rock You',
  text: 'The Ben Elton musical opens in London\u2019s West End and runs for twelve years, taking the catalogue to the stage.',
  more: 'It went on to be staged in dozens of countries, introducing the songs to a new theatre-going audience.',
  img: null
}, {
  year: '2012',
  cat: 'live',
  title: 'A new voice',
  text: 'Queen + Adam Lambert play their first shows together; May and Taylor perform at the London Olympics closing ceremony.',
  more: 'The Lambert partnership would grow into sold-out world tours across the following decade.',
  img: null
}, {
  year: '2018',
  cat: 'milestone',
  title: 'Bohemian Rhapsody',
  text: 'The biopic becomes the highest-grossing music film ever made and goes on to win four Academy Awards.',
  more: 'A new generation discovered the band; catalogue streaming and sales surged around the release.',
  img: null
}, {
  year: '2024',
  cat: 'milestone',
  title: 'A lasting legacy',
  text: 'Reissues, streaming records and a landmark catalogue deal carry the music \u2014 and this community \u2014 to new generations.',
  more: 'PLEASE VERIFY: the reported catalogue acquisition figure and date before publishing this entry.',
  img: null
}];

// Decade buckets for grouping + jump navigation.
window.QZ_DECADES = ['1970s', '1980s', '1990s', '2000s', '2010s', '2020s'];
window.qzDecadeOf = function (year) {
  return year.slice(0, 3) + '0s';
};
})(); } catch (e) { __ds_ns.__errors.push({ path: "design_handoff_timeline/tl-data.js", error: String((e && e.message) || e) }); }

// design_handoff_timeline/tl-shared.jsx
try { (() => {
// Shared timeline helpers — category tags, filter chips, reveal-on-scroll, photo frame.
const {
  useState,
  useEffect,
  useRef
} = React;

// Small uppercase category tag (Cinzel), coloured by meaning.
function CatTag({
  cat,
  onDark
}) {
  const c = window.QZ_CATS[cat];
  if (!c) return null;
  return /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: 7,
      font: "var(--fw-semibold) 10px/1 var(--font-titling)",
      letterSpacing: '0.18em',
      textTransform: 'uppercase',
      color: onDark ? '#fff' : c.deep
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      width: 7,
      height: 7,
      borderRadius: '50%',
      background: c.color,
      boxShadow: onDark ? '0 0 0 3px rgba(255,255,255,0.08)' : 'none'
    }
  }), c.label);
}

// Filter chips — All + one per category. `onDark` swaps to the dark surface styling.
function FilterChips({
  active,
  onChange,
  onDark
}) {
  const cats = Object.keys(window.QZ_CATS);
  const base = {
    font: "var(--fw-medium) 12px/1 var(--font-body)",
    letterSpacing: '0.04em',
    padding: '9px 15px',
    borderRadius: 2,
    cursor: 'pointer',
    background: 'none',
    transition: 'all 200ms ease',
    whiteSpace: 'nowrap'
  };
  const chip = (key, label, color) => {
    const on = active === key;
    const idle = onDark ? 'rgba(255,255,255,0.55)' : 'var(--text-secondary)';
    const bd = onDark ? 'rgba(255,255,255,0.18)' : 'var(--border-strong)';
    return /*#__PURE__*/React.createElement("button", {
      key: key,
      onClick: () => onChange(key),
      style: {
        ...base,
        border: '1px solid ' + (on ? color || (onDark ? '#fff' : 'var(--qz-charcoal)') : bd),
        color: on ? onDark ? '#fff' : color ? color : 'var(--qz-charcoal)' : idle,
        background: on ? onDark ? 'rgba(255,255,255,0.06)' : color ? 'transparent' : 'transparent' : 'transparent',
        fontWeight: on ? 600 : 500
      }
    }, key !== 'all' && /*#__PURE__*/React.createElement("span", {
      style: {
        display: 'inline-block',
        width: 7,
        height: 7,
        borderRadius: '50%',
        background: color,
        marginRight: 8,
        verticalAlign: 'middle'
      }
    }), label);
  };
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      gap: 10,
      alignItems: 'center'
    }
  }, chip('all', 'All events', null), cats.map(k => chip(k, window.QZ_CATS[k].label, window.QZ_CATS[k].color)));
}

// Reveal-on-scroll wrapper. Base state is VISIBLE (print / reduced-motion / no-JS safe);
// animates FROM hidden only when motion is allowed and the observer fires.
function Reveal({
  children,
  y = 22,
  delay = 0,
  style
}) {
  const ref = useRef(null);
  const reduce = typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const [shown, setShown] = useState(reduce);
  useEffect(() => {
    if (reduce || !ref.current || !('IntersectionObserver' in window)) {
      setShown(true);
      return;
    }
    const ob = new IntersectionObserver(es => {
      es.forEach(e => {
        if (e.isIntersecting) {
          setShown(true);
          ob.disconnect();
        }
      });
    }, {
      threshold: 0.15,
      rootMargin: '0px 0px -8% 0px'
    });
    ob.observe(ref.current);
    return () => ob.disconnect();
  }, [reduce]);
  return /*#__PURE__*/React.createElement("div", {
    ref: ref,
    style: {
      ...style,
      opacity: shown ? 1 : 0,
      transform: shown ? 'none' : 'translateY(' + y + 'px)',
      transition: 'opacity 700ms cubic-bezier(.22,.61,.36,1) ' + delay + 'ms, transform 700ms cubic-bezier(.22,.61,.36,1) ' + delay + 'ms'
    }
  }, children);
}

// Archival photo frame. Real asset (greyscaled) or an on-brand placeholder.
function EventPhoto({
  img,
  title,
  ratio = '4 / 3',
  rounded = 2
}) {
  const frame = {
    position: 'relative',
    width: '100%',
    aspectRatio: ratio,
    overflow: 'hidden',
    borderRadius: rounded,
    border: '1px solid var(--hairline)',
    background: 'var(--qz-grey-100)'
  };
  if (img) {
    return /*#__PURE__*/React.createElement("div", {
      style: frame
    }, /*#__PURE__*/React.createElement("img", {
      src: img,
      alt: title,
      style: {
        width: '100%',
        height: '100%',
        objectFit: 'cover',
        filter: 'grayscale(1) contrast(1.02)'
      }
    }));
  }
  return /*#__PURE__*/React.createElement("div", {
    style: {
      ...frame,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      background: 'repeating-linear-gradient(135deg, #ECEBE6 0 14px, #F3F2EE 14px 28px)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      textAlign: 'center',
      color: 'var(--qz-grey-500)'
    }
  }, /*#__PURE__*/React.createElement("svg", {
    width: "26",
    height: "26",
    viewBox: "0 0 24 24",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: "1.4",
    style: {
      margin: '0 auto 6px'
    }
  }, /*#__PURE__*/React.createElement("rect", {
    x: "3",
    y: "5",
    width: "18",
    height: "14",
    rx: "1.5"
  }), /*#__PURE__*/React.createElement("circle", {
    cx: "12",
    cy: "12",
    r: "3.2"
  }), /*#__PURE__*/React.createElement("path", {
    d: "M8 5l1.5-2h5L16 5"
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      font: "var(--fw-semibold) 9px/1 var(--font-titling)",
      letterSpacing: '0.2em',
      textTransform: 'uppercase'
    }
  }, "Archive photo")));
}
Object.assign(window, {
  CatTag,
  FilterChips,
  Reveal,
  EventPhoto
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "design_handoff_timeline/tl-shared.jsx", error: String((e && e.message) || e) }); }

// design_handoff_timeline/tl-variant-a.jsx
try { (() => {
// "The Decades" — vertical, decade-grouped, built to scroll through hundreds of events.
// Compact rows by default (year · node · tag · title · one-line lede); click any row to
// expand the full card (standfirst, archival photo, read-the-story). A sticky rail jumps
// by decade AND by year within the active decade, and tracks position as you scroll.
const {
  useState: useStateA,
  useEffect: useEffectA,
  useRef: useRefA,
  useMemo: useMemoA
} = React;
function DecadeRail({
  groups,
  activeDecade,
  activeYear,
  counts,
  onJumpDecade,
  onJumpYear
}) {
  return /*#__PURE__*/React.createElement("nav", {
    style: {
      position: 'sticky',
      top: 96,
      alignSelf: 'start',
      maxHeight: 'calc(100vh - 120px)',
      overflowY: 'auto',
      paddingRight: 8,
      display: 'flex',
      flexDirection: 'column'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      font: "var(--fw-semibold) 10px/1 var(--font-titling)",
      letterSpacing: '0.2em',
      textTransform: 'uppercase',
      color: 'var(--qz-grey-500)',
      marginBottom: 14,
      paddingLeft: 16
    }
  }, "Jump to"), groups.map(g => {
    const on = activeDecade === g.d;
    return /*#__PURE__*/React.createElement("div", {
      key: g.d,
      style: {
        marginBottom: 2
      }
    }, /*#__PURE__*/React.createElement("button", {
      onClick: () => onJumpDecade(g.d),
      style: {
        display: 'flex',
        alignItems: 'baseline',
        gap: 9,
        textAlign: 'left',
        width: '100%',
        background: 'none',
        border: 'none',
        cursor: 'pointer',
        padding: '7px 0 7px 16px',
        position: 'relative',
        font: "var(--fw-medium) 17px/1 var(--font-titling)",
        letterSpacing: '0.06em',
        color: on ? 'var(--qz-gold-deep)' : 'var(--text-secondary)',
        transition: 'color 200ms ease'
      }
    }, /*#__PURE__*/React.createElement("span", {
      style: {
        position: 'absolute',
        left: 0,
        top: '50%',
        transform: 'translateY(-50%)',
        width: 2,
        height: on ? 20 : 0,
        background: 'var(--qz-gold)',
        transition: 'height 220ms ease'
      }
    }), g.d, /*#__PURE__*/React.createElement("span", {
      style: {
        font: "var(--fw-medium) 11px/1 var(--font-body)",
        color: 'var(--qz-grey-400)'
      }
    }, counts[g.d])), /*#__PURE__*/React.createElement("div", {
      style: {
        overflow: 'hidden',
        maxHeight: on ? g.years.length * 30 + 8 : 0,
        transition: 'max-height 320ms cubic-bezier(.22,.61,.36,1)'
      }
    }, /*#__PURE__*/React.createElement("div", {
      style: {
        paddingLeft: 16,
        margin: '2px 0 8px',
        borderLeft: '1px solid var(--hairline)'
      }
    }, g.years.map(y => {
      const yon = activeYear === y;
      return /*#__PURE__*/React.createElement("button", {
        key: y,
        onClick: () => onJumpYear(y),
        style: {
          display: 'block',
          textAlign: 'left',
          width: '100%',
          background: 'none',
          border: 'none',
          cursor: 'pointer',
          padding: '5px 0 5px 14px',
          position: 'relative',
          font: (yon ? 'var(--fw-semibold)' : 'var(--fw-regular)') + " 12px/1 var(--font-body)",
          letterSpacing: '0.04em',
          color: yon ? 'var(--qz-gold-deep)' : 'var(--qz-grey-500)',
          transition: 'color 160ms ease'
        }
      }, /*#__PURE__*/React.createElement("span", {
        style: {
          position: 'absolute',
          left: -1,
          top: '50%',
          transform: 'translateY(-50%)',
          width: yon ? 8 : 0,
          height: 2,
          background: 'var(--qz-gold)',
          transition: 'width 180ms ease'
        }
      }), y);
    }))));
  }));
}
function EventRowA({
  ev,
  first
}) {
  const [open, setOpen] = useStateA(false);
  const c = window.QZ_CATS[ev.cat];
  return /*#__PURE__*/React.createElement("div", {
    "data-year": ev.year,
    style: {
      position: 'relative',
      paddingLeft: 34
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      position: 'absolute',
      left: 5,
      top: first ? 22 : 0,
      bottom: 0,
      width: 1,
      background: 'var(--hairline)'
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      position: 'absolute',
      left: 0,
      top: 20,
      width: 11,
      height: 11,
      borderRadius: '50%',
      background: c.color,
      boxShadow: '0 0 0 4px var(--qz-warm-white)',
      zIndex: 1
    }
  }), /*#__PURE__*/React.createElement("button", {
    onClick: () => setOpen(!open),
    style: {
      display: 'grid',
      gridTemplateColumns: '78px 1fr auto',
      alignItems: 'baseline',
      gap: 'clamp(14px,2.2vw,34px)',
      width: '100%',
      textAlign: 'left',
      background: 'none',
      border: 'none',
      cursor: 'pointer',
      padding: '13px 0',
      borderTop: first ? 'none' : '1px solid var(--hairline)'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      font: "var(--fw-medium) 21px/1 var(--font-titling)",
      letterSpacing: '0.02em',
      color: 'var(--qz-charcoal)'
    }
  }, ev.year), /*#__PURE__*/React.createElement("span", {
    style: {
      minWidth: 0
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'block',
      lineHeight: 1.25
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'inline-flex',
      verticalAlign: '0.12em',
      marginRight: 14
    }
  }, /*#__PURE__*/React.createElement(CatTag, {
    cat: ev.cat
  })), /*#__PURE__*/React.createElement("span", {
    style: {
      font: "var(--fw-semibold) 20px/1.25 var(--font-display)",
      color: 'var(--text-primary)'
    }
  }, ev.title)), !open && /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'block',
      font: "var(--fw-regular) 15px/1.5 var(--font-body)",
      color: 'var(--text-muted)',
      margin: '5px 0 0',
      maxWidth: 620,
      overflow: 'hidden',
      textOverflow: 'ellipsis',
      whiteSpace: 'nowrap'
    }
  }, ev.text)), /*#__PURE__*/React.createElement("svg", {
    width: "15",
    height: "15",
    viewBox: "0 0 24 24",
    fill: "none",
    stroke: "var(--qz-grey-500)",
    strokeWidth: "2",
    style: {
      marginTop: 6,
      transform: open ? 'rotate(180deg)' : 'none',
      transition: 'transform 300ms ease',
      flexShrink: 0
    }
  }, /*#__PURE__*/React.createElement("path", {
    d: "m6 9 6 6 6-6"
  }))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateRows: open ? '1fr' : '0fr',
      transition: 'grid-template-rows 380ms cubic-bezier(.22,.61,.36,1)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      overflow: 'hidden'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: '78px 1fr',
      gap: 'clamp(14px,2.2vw,34px)',
      paddingBottom: 26
    }
  }, /*#__PURE__*/React.createElement("span", null), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: ev.img ? '1fr 190px' : '1fr',
      gap: 28,
      alignItems: 'start',
      maxWidth: ev.img ? 'none' : 640
    }
  }, /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement("p", {
    style: {
      font: "var(--fw-regular) 16px/1.62 var(--font-body)",
      color: 'var(--text-secondary)',
      margin: '0 0 12px'
    }
  }, ev.text), /*#__PURE__*/React.createElement("p", {
    style: {
      font: "var(--fw-regular) 15px/1.6 var(--font-body)",
      color: 'var(--text-secondary)',
      margin: 0,
      paddingTop: 12,
      borderTop: '1px solid var(--hairline)'
    }
  }, ev.more), /*#__PURE__*/React.createElement("a", {
    href: "#",
    onClick: e => e.preventDefault(),
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: 6,
      marginTop: 16,
      font: "var(--fw-semibold) 12px/1 var(--font-body)",
      letterSpacing: '0.08em',
      textTransform: 'uppercase',
      color: 'var(--link)',
      textDecoration: 'none'
    }
  }, "Read the story", /*#__PURE__*/React.createElement("svg", {
    width: "13",
    height: "13",
    viewBox: "0 0 24 24",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: "2"
  }, /*#__PURE__*/React.createElement("path", {
    d: "M5 12h14M13 6l6 6-6 6"
  })))), ev.img && /*#__PURE__*/React.createElement(EventPhoto, {
    img: ev.img,
    title: ev.title,
    ratio: "1 / 1"
  }))))));
}
function TimelineDecades() {
  const [filter, setFilter] = useStateA('all');
  const [activeDecade, setActiveDecade] = useStateA(window.QZ_DECADES[0]);
  const [activeYear, setActiveYear] = useStateA(null);
  const secRefs = useRefA({});
  const rootRef = useRefA(null);
  const all = window.QZ_TIMELINE;
  const events = filter === 'all' ? all : all.filter(e => e.cat === filter);

  // group by decade, with the sorted unique years present in each
  const groups = useMemoA(() => {
    return window.QZ_DECADES.map(d => {
      const items = events.filter(e => window.qzDecadeOf(e.year) === d);
      const years = [...new Set(items.map(e => e.year))].sort();
      return {
        d,
        items,
        years
      };
    }).filter(g => g.items.length);
  }, [filter]);
  const counts = {};
  groups.forEach(g => {
    counts[g.d] = g.items.length;
  });

  // track active decade + year as the reader scrolls
  useEffectA(() => {
    const decObs = new IntersectionObserver(es => {
      es.forEach(e => {
        if (e.isIntersecting) setActiveDecade(e.target.getAttribute('data-decade'));
      });
    }, {
      rootMargin: '-25% 0px -70% 0px'
    });
    Object.values(secRefs.current).forEach(el => el && decObs.observe(el));
    const rows = rootRef.current ? rootRef.current.querySelectorAll('[data-year]') : [];
    const yearObs = new IntersectionObserver(es => {
      es.forEach(e => {
        if (e.isIntersecting) setActiveYear(e.target.getAttribute('data-year'));
      });
    }, {
      rootMargin: '-30% 0px -65% 0px'
    });
    rows.forEach(el => yearObs.observe(el));
    return () => {
      decObs.disconnect();
      yearObs.disconnect();
    };
  }, [filter, groups.length]);
  const jumpDecade = d => {
    const el = secRefs.current[d];
    if (el) window.scrollTo({
      top: el.getBoundingClientRect().top + window.pageYOffset - 84,
      behavior: 'smooth'
    });
  };
  const jumpYear = y => {
    const el = rootRef.current && rootRef.current.querySelector('[data-year="' + y + '"]');
    if (el) window.scrollTo({
      top: el.getBoundingClientRect().top + window.pageYOffset - 96,
      behavior: 'smooth'
    });
  };
  return /*#__PURE__*/React.createElement("div", {
    style: {
      background: 'var(--qz-warm-white)',
      minHeight: '100vh'
    }
  }, /*#__PURE__*/React.createElement("header", {
    style: {
      position: 'relative',
      background: 'var(--qz-black)',
      overflow: 'hidden',
      padding: 'clamp(70px,9vw,120px) var(--gutter-lg) clamp(56px,7vw,90px)'
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: "assets/crest-white.png",
    alt: "",
    style: {
      position: 'absolute',
      top: '50%',
      right: '4%',
      transform: 'translateY(-50%)',
      width: 260,
      opacity: 0.06
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 1180,
      margin: '0 auto',
      position: 'relative'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      font: "var(--fw-semibold) 12px/1 var(--font-titling)",
      letterSpacing: '0.22em',
      textTransform: 'uppercase',
      color: 'var(--qz-gold)',
      marginBottom: 20
    }
  }, "Five decades \xB7 1970 \u2013 today"), /*#__PURE__*/React.createElement("h1", {
    style: {
      font: "var(--fw-regular) clamp(48px,7vw,88px)/1 var(--font-display)",
      letterSpacing: '-0.015em',
      color: '#fff',
      margin: '0 0 22px'
    }
  }, "The Queen Timeline"), /*#__PURE__*/React.createElement("p", {
    style: {
      font: "var(--fw-regular) 20px/1.55 var(--font-body)",
      color: 'rgba(255,255,255,0.78)',
      margin: 0,
      maxWidth: 620
    }
  }, "The story of the band, year by year \u2014 a guided path through the music, the performances and the moments that gathered a community."))), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'sticky',
      top: 0,
      zIndex: 20,
      background: 'rgba(247,246,243,0.92)',
      backdropFilter: 'saturate(180%) blur(10px)',
      borderBottom: '1px solid var(--hairline)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 1180,
      margin: '0 auto',
      padding: '14px var(--gutter-lg)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: 20,
      flexWrap: 'wrap'
    }
  }, /*#__PURE__*/React.createElement(FilterChips, {
    active: filter,
    onChange: setFilter
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      font: "var(--fw-medium) 12px/1 var(--font-body)",
      letterSpacing: '0.04em',
      color: 'var(--text-muted)'
    }
  }, events.length, " ", events.length === 1 ? 'event' : 'events'))), /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 1180,
      margin: '0 auto',
      padding: 'clamp(44px,5vw,72px) var(--gutter-lg) 140px',
      display: 'grid',
      gridTemplateColumns: '182px 1fr',
      gap: 'clamp(24px,4vw,72px)'
    }
  }, /*#__PURE__*/React.createElement(DecadeRail, {
    groups: groups,
    activeDecade: activeDecade,
    activeYear: activeYear,
    counts: counts,
    onJumpDecade: jumpDecade,
    onJumpYear: jumpYear
  }), /*#__PURE__*/React.createElement("div", {
    ref: rootRef
  }, groups.map(g => /*#__PURE__*/React.createElement("section", {
    key: g.d,
    "data-decade": g.d,
    ref: el => secRefs.current[g.d] = el,
    style: {
      marginBottom: 52
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'baseline',
      gap: 16,
      marginBottom: 20
    }
  }, /*#__PURE__*/React.createElement("h2", {
    style: {
      font: "var(--fw-medium) 38px/1 var(--font-display)",
      color: 'var(--qz-charcoal)',
      margin: 0
    }
  }, g.d), /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1,
      height: 1,
      background: 'var(--border-strong)'
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      font: "var(--fw-medium) 12px/1 var(--font-body)",
      letterSpacing: '0.04em',
      color: 'var(--text-muted)'
    }
  }, g.items.length)), /*#__PURE__*/React.createElement("div", null, g.items.map((ev, i) => /*#__PURE__*/React.createElement(EventRowA, {
    key: ev.year + ev.title,
    ev: ev,
    first: i === 0
  }))))), !groups.length && /*#__PURE__*/React.createElement("p", {
    style: {
      color: 'var(--text-muted)',
      font: 'var(--type-body)'
    }
  }, "No events in this category."))));
}
window.TimelineDecades = TimelineDecades;
})(); } catch (e) { __ds_ns.__errors.push({ path: "design_handoff_timeline/tl-variant-a.jsx", error: String((e && e.message) || e) }); }

// explorations/timeline/tl-app.jsx
try { (() => {
// App shell — mounts the Decades timeline (the direction taken forward for scale).
ReactDOM.createRoot(document.getElementById('root')).render(/*#__PURE__*/React.createElement(TimelineDecades, null));
})(); } catch (e) { __ds_ns.__errors.push({ path: "explorations/timeline/tl-app.jsx", error: String((e && e.message) || e) }); }

// explorations/timeline/tl-data.js
try { (() => {
// Queenzone Timeline — researched event data (1970 → today).
// PLEASE VERIFY the facts/dates; the final 2024 entry especially (catalogue deal).
// Category → brand accent:
//   music     Royal Blue     releases & recordings
//   live      Royal Purple   tours & landmark performances
//   milestone Antique Gold   formation, honours, cultural landmarks (rarest accent)
//   loss      Burgundy       Freddie's passing & farewell
// NOTE: the questionnaire listed a "Community" category — I folded the two
// community/fan moments (Tribute Concert, Queenzone founding) into "milestone"
// to keep the palette to four accents. Say the word and I'll split it back out.

window.QZ_CATS = {
  music: {
    label: 'Music',
    color: 'var(--qz-blue)',
    tint: 'var(--qz-blue-tint)',
    deep: 'var(--qz-blue-deep)'
  },
  live: {
    label: 'Live',
    color: 'var(--qz-purple)',
    tint: 'var(--qz-purple-tint)',
    deep: 'var(--qz-purple-deep)'
  },
  milestone: {
    label: 'Milestone',
    color: 'var(--qz-gold-deep)',
    tint: 'var(--qz-gold-tint)',
    deep: 'var(--qz-gold-deep)'
  },
  loss: {
    label: 'Loss',
    color: 'var(--qz-burgundy)',
    tint: 'var(--qz-burgundy-tint)',
    deep: 'var(--qz-burgundy-deep)'
  }
};

// img: a real asset path (greyscaled in-page) or null for an archival placeholder.
window.QZ_TIMELINE = [{
  year: '1970',
  cat: 'milestone',
  title: 'Queen is born',
  text: 'Freddie Bulsara joins Brian May and Roger Taylor\u2019s band Smile, renames it Queen and designs the regal crest that still anchors everything.',
  more: 'The first billed performance as Queen takes place in 1970; Freddie adopts the surname Mercury soon after.',
  img: null
}, {
  year: '1971',
  cat: 'milestone',
  title: 'The classic line-up',
  text: 'John Deacon joins on bass, completing the four-piece that would stay intact for twenty years.',
  more: 'Deacon was the last of some sixty bassists the band auditioned \u2014 chosen as much for his quiet steadiness as his playing.',
  img: null
}, {
  year: '1973',
  cat: 'music',
  title: 'The debut album',
  text: 'Queen release their self-titled debut on 13 July 1973, recorded largely in stolen night-time hours at Trident Studios.',
  more: 'Much of it was cut using downtime the band were given in exchange for being \u201Cguinea pigs\u201D for new studio equipment.',
  img: null
}, {
  year: '1974',
  cat: 'music',
  title: 'Killer Queen',
  text: '\u2018Sheer Heart Attack\u2019 and the \u2018Killer Queen\u2019 single deliver the band\u2019s first major chart success on both sides of the Atlantic.',
  more: 'Written by Freddie, \u2018Killer Queen\u2019 reached No. 2 in the UK and announced the band\u2019s theatrical ambition.',
  img: null
}, {
  year: '1975',
  cat: 'music',
  title: 'A Night at the Opera',
  text: 'The lavish album \u2014 and \u2018Bohemian Rhapsody\u2019, UK No. 1 for nine weeks \u2014 rewrite the rules of the rock single.',
  more: 'Reputedly the most expensive album ever made to that point; its promo film is often credited as pop\u2019s first true music video.',
  img: null
}, {
  year: '1977',
  cat: 'music',
  title: 'We Will Rock You',
  text: '\u2018News of the World\u2019 gives the world two of the most-played stadium anthems ever written, back to back on side one.',
  more: '\u2018We Will Rock You\u2019 and \u2018We Are the Champions\u2019 were designed for crowds to sing \u2014 and have been sung ever since.',
  img: null
}, {
  year: '1980',
  cat: 'music',
  title: 'The Game',
  text: 'Their first US No. 1 album, powered by \u2018Crazy Little Thing Called Love\u2019 and \u2018Another One Bites the Dust\u2019.',
  more: 'The first Queen album to use a synthesiser \u2014 a deliberate break from the \u201Cno synths\u201D note printed on earlier sleeves.',
  img: null
}, {
  year: '1981',
  cat: 'music',
  title: 'Greatest Hits',
  text: 'The compilation becomes the best-selling album in British history; the band play vast South American stadiums.',
  more: 'It has since sold many millions of copies and is a fixture of \u201Cbest-selling albums of all time\u201D lists.',
  img: null
}, {
  year: '1985',
  cat: 'live',
  title: 'Live Aid',
  text: 'On 13 July at Wembley, twenty-one minutes widely regarded as the greatest live performance in rock.',
  more: 'The set was tightly rehearsed and played to the back row; it revived the band\u2019s fortunes overnight.',
  img: '../../assets/img-hero.jpg'
}, {
  year: '1986',
  cat: 'live',
  title: 'The Magic Tour',
  text: 'The final tour with all four members draws record crowds; the last show with Freddie takes place at Knebworth on 9 August.',
  more: 'No one knew at the time that Knebworth would be the last time the classic line-up played live together.',
  img: '../../assets/img-crowd.jpg'
}, {
  year: '1991',
  cat: 'loss',
  title: 'Innuendo & farewell',
  text: 'The \u2018Innuendo\u2019 album tops the UK chart. On 24 November, Freddie Mercury dies, a day after confirming his illness.',
  more: '\u2018The Show Must Go On\u2019 \u2014 recorded when he could barely stand \u2014 became a defiant epitaph.',
  img: '../../assets/img-portrait.jpg'
}, {
  year: '1992',
  cat: 'milestone',
  title: 'The Tribute Concert',
  text: 'On 20 April, 72,000 fill Wembley for The Freddie Mercury Tribute Concert, raising awareness and funds for AIDS.',
  more: 'Broadcast worldwide to an estimated billion viewers, it launched the Mercury Phoenix Trust.',
  img: null
}, {
  year: '1995',
  cat: 'music',
  title: 'Made in Heaven',
  text: 'The posthumous final album, built around Freddie\u2019s last vocals, is released in November.',
  more: 'The surviving members completed the recordings from sessions Freddie insisted on making while he still could.',
  img: null
}, {
  year: '1999',
  cat: 'milestone',
  title: 'Queenzone.com',
  text: 'The online fan community that would become this archive is founded \u2014 the beginning of two decades of gathering.',
  more: 'From hand-coded HTML to a 100,000-post forum, Queenzone became one of the definitive fan homes on the web.',
  img: null
}, {
  year: '2001',
  cat: 'milestone',
  title: 'Rock and Roll Hall of Fame',
  text: 'Queen are inducted, formally recognising their place in the history of popular music.',
  more: 'A run of honours followed through the 2000s, including songwriting and industry lifetime awards.',
  img: null
}, {
  year: '2002',
  cat: 'milestone',
  title: 'We Will Rock You',
  text: 'The Ben Elton musical opens in London\u2019s West End and runs for twelve years, taking the catalogue to the stage.',
  more: 'It went on to be staged in dozens of countries, introducing the songs to a new theatre-going audience.',
  img: null
}, {
  year: '2012',
  cat: 'live',
  title: 'A new voice',
  text: 'Queen + Adam Lambert play their first shows together; May and Taylor perform at the London Olympics closing ceremony.',
  more: 'The Lambert partnership would grow into sold-out world tours across the following decade.',
  img: null
}, {
  year: '2018',
  cat: 'milestone',
  title: 'Bohemian Rhapsody',
  text: 'The biopic becomes the highest-grossing music film ever made and goes on to win four Academy Awards.',
  more: 'A new generation discovered the band; catalogue streaming and sales surged around the release.',
  img: null
}, {
  year: '2024',
  cat: 'milestone',
  title: 'A lasting legacy',
  text: 'Reissues, streaming records and a landmark catalogue deal carry the music \u2014 and this community \u2014 to new generations.',
  more: 'PLEASE VERIFY: the reported catalogue acquisition figure and date before publishing this entry.',
  img: null
}];

// Decade buckets for grouping + jump navigation.
window.QZ_DECADES = ['1970s', '1980s', '1990s', '2000s', '2010s', '2020s'];
window.qzDecadeOf = function (year) {
  return year.slice(0, 3) + '0s';
};
})(); } catch (e) { __ds_ns.__errors.push({ path: "explorations/timeline/tl-data.js", error: String((e && e.message) || e) }); }

// explorations/timeline/tl-shared.jsx
try { (() => {
// Shared timeline helpers — category tags, filter chips, reveal-on-scroll, photo frame.
const {
  useState,
  useEffect,
  useRef
} = React;

// Small uppercase category tag (Cinzel), coloured by meaning.
function CatTag({
  cat,
  onDark
}) {
  const c = window.QZ_CATS[cat];
  if (!c) return null;
  return /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: 7,
      font: "var(--fw-semibold) 10px/1 var(--font-titling)",
      letterSpacing: '0.18em',
      textTransform: 'uppercase',
      color: onDark ? '#fff' : c.deep
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      width: 7,
      height: 7,
      borderRadius: '50%',
      background: c.color,
      boxShadow: onDark ? '0 0 0 3px rgba(255,255,255,0.08)' : 'none'
    }
  }), c.label);
}

// Filter chips — All + one per category. `onDark` swaps to the dark surface styling.
function FilterChips({
  active,
  onChange,
  onDark
}) {
  const cats = Object.keys(window.QZ_CATS);
  const base = {
    font: "var(--fw-medium) 12px/1 var(--font-body)",
    letterSpacing: '0.04em',
    padding: '9px 15px',
    borderRadius: 2,
    cursor: 'pointer',
    background: 'none',
    transition: 'all 200ms ease',
    whiteSpace: 'nowrap'
  };
  const chip = (key, label, color) => {
    const on = active === key;
    const idle = onDark ? 'rgba(255,255,255,0.55)' : 'var(--text-secondary)';
    const bd = onDark ? 'rgba(255,255,255,0.18)' : 'var(--border-strong)';
    return /*#__PURE__*/React.createElement("button", {
      key: key,
      onClick: () => onChange(key),
      style: {
        ...base,
        border: '1px solid ' + (on ? color || (onDark ? '#fff' : 'var(--qz-charcoal)') : bd),
        color: on ? onDark ? '#fff' : color ? color : 'var(--qz-charcoal)' : idle,
        background: on ? onDark ? 'rgba(255,255,255,0.06)' : color ? 'transparent' : 'transparent' : 'transparent',
        fontWeight: on ? 600 : 500
      }
    }, key !== 'all' && /*#__PURE__*/React.createElement("span", {
      style: {
        display: 'inline-block',
        width: 7,
        height: 7,
        borderRadius: '50%',
        background: color,
        marginRight: 8,
        verticalAlign: 'middle'
      }
    }), label);
  };
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      gap: 10,
      alignItems: 'center'
    }
  }, chip('all', 'All events', null), cats.map(k => chip(k, window.QZ_CATS[k].label, window.QZ_CATS[k].color)));
}

// Reveal-on-scroll wrapper. Base state is VISIBLE (print / reduced-motion / no-JS safe);
// animates FROM hidden only when motion is allowed and the observer fires.
function Reveal({
  children,
  y = 22,
  delay = 0,
  style
}) {
  const ref = useRef(null);
  const reduce = typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const [shown, setShown] = useState(reduce);
  useEffect(() => {
    if (reduce || !ref.current || !('IntersectionObserver' in window)) {
      setShown(true);
      return;
    }
    const ob = new IntersectionObserver(es => {
      es.forEach(e => {
        if (e.isIntersecting) {
          setShown(true);
          ob.disconnect();
        }
      });
    }, {
      threshold: 0.15,
      rootMargin: '0px 0px -8% 0px'
    });
    ob.observe(ref.current);
    return () => ob.disconnect();
  }, [reduce]);
  return /*#__PURE__*/React.createElement("div", {
    ref: ref,
    style: {
      ...style,
      opacity: shown ? 1 : 0,
      transform: shown ? 'none' : 'translateY(' + y + 'px)',
      transition: 'opacity 700ms cubic-bezier(.22,.61,.36,1) ' + delay + 'ms, transform 700ms cubic-bezier(.22,.61,.36,1) ' + delay + 'ms'
    }
  }, children);
}

// Archival photo frame. Real asset (greyscaled) or an on-brand placeholder.
function EventPhoto({
  img,
  title,
  ratio = '4 / 3',
  rounded = 2
}) {
  const frame = {
    position: 'relative',
    width: '100%',
    aspectRatio: ratio,
    overflow: 'hidden',
    borderRadius: rounded,
    border: '1px solid var(--hairline)',
    background: 'var(--qz-grey-100)'
  };
  if (img) {
    return /*#__PURE__*/React.createElement("div", {
      style: frame
    }, /*#__PURE__*/React.createElement("img", {
      src: img,
      alt: title,
      style: {
        width: '100%',
        height: '100%',
        objectFit: 'cover',
        filter: 'grayscale(1) contrast(1.02)'
      }
    }));
  }
  return /*#__PURE__*/React.createElement("div", {
    style: {
      ...frame,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      background: 'repeating-linear-gradient(135deg, #ECEBE6 0 14px, #F3F2EE 14px 28px)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      textAlign: 'center',
      color: 'var(--qz-grey-500)'
    }
  }, /*#__PURE__*/React.createElement("svg", {
    width: "26",
    height: "26",
    viewBox: "0 0 24 24",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: "1.4",
    style: {
      margin: '0 auto 6px'
    }
  }, /*#__PURE__*/React.createElement("rect", {
    x: "3",
    y: "5",
    width: "18",
    height: "14",
    rx: "1.5"
  }), /*#__PURE__*/React.createElement("circle", {
    cx: "12",
    cy: "12",
    r: "3.2"
  }), /*#__PURE__*/React.createElement("path", {
    d: "M8 5l1.5-2h5L16 5"
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      font: "var(--fw-semibold) 9px/1 var(--font-titling)",
      letterSpacing: '0.2em',
      textTransform: 'uppercase'
    }
  }, "Archive photo")));
}
Object.assign(window, {
  CatTag,
  FilterChips,
  Reveal,
  EventPhoto
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "explorations/timeline/tl-shared.jsx", error: String((e && e.message) || e) }); }

// explorations/timeline/tl-variant-a.jsx
try { (() => {
// "The Decades" — vertical, decade-grouped, built to scroll through hundreds of events.
// Compact rows by default (year · node · tag · title · one-line lede); click any row to
// expand the full card (standfirst, archival photo, read-the-story). A sticky rail jumps
// by decade AND by year within the active decade, and tracks position as you scroll.
const {
  useState: useStateA,
  useEffect: useEffectA,
  useRef: useRefA,
  useMemo: useMemoA
} = React;
function DecadeRail({
  groups,
  activeDecade,
  activeYear,
  counts,
  onJumpDecade,
  onJumpYear
}) {
  return /*#__PURE__*/React.createElement("nav", {
    style: {
      position: 'sticky',
      top: 96,
      alignSelf: 'start',
      maxHeight: 'calc(100vh - 120px)',
      overflowY: 'auto',
      paddingRight: 8,
      display: 'flex',
      flexDirection: 'column'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      font: "var(--fw-semibold) 10px/1 var(--font-titling)",
      letterSpacing: '0.2em',
      textTransform: 'uppercase',
      color: 'var(--qz-grey-500)',
      marginBottom: 14,
      paddingLeft: 16
    }
  }, "Jump to"), groups.map(g => {
    const on = activeDecade === g.d;
    return /*#__PURE__*/React.createElement("div", {
      key: g.d,
      style: {
        marginBottom: 2
      }
    }, /*#__PURE__*/React.createElement("button", {
      onClick: () => onJumpDecade(g.d),
      style: {
        display: 'flex',
        alignItems: 'baseline',
        gap: 9,
        textAlign: 'left',
        width: '100%',
        background: 'none',
        border: 'none',
        cursor: 'pointer',
        padding: '7px 0 7px 16px',
        position: 'relative',
        font: "var(--fw-medium) 17px/1 var(--font-titling)",
        letterSpacing: '0.06em',
        color: on ? 'var(--qz-gold-deep)' : 'var(--text-secondary)',
        transition: 'color 200ms ease'
      }
    }, /*#__PURE__*/React.createElement("span", {
      style: {
        position: 'absolute',
        left: 0,
        top: '50%',
        transform: 'translateY(-50%)',
        width: 2,
        height: on ? 20 : 0,
        background: 'var(--qz-gold)',
        transition: 'height 220ms ease'
      }
    }), g.d, /*#__PURE__*/React.createElement("span", {
      style: {
        font: "var(--fw-medium) 11px/1 var(--font-body)",
        color: 'var(--qz-grey-400)'
      }
    }, counts[g.d])), /*#__PURE__*/React.createElement("div", {
      style: {
        overflow: 'hidden',
        maxHeight: on ? g.years.length * 30 + 8 : 0,
        transition: 'max-height 320ms cubic-bezier(.22,.61,.36,1)'
      }
    }, /*#__PURE__*/React.createElement("div", {
      style: {
        paddingLeft: 16,
        margin: '2px 0 8px',
        borderLeft: '1px solid var(--hairline)'
      }
    }, g.years.map(y => {
      const yon = activeYear === y;
      return /*#__PURE__*/React.createElement("button", {
        key: y,
        onClick: () => onJumpYear(y),
        style: {
          display: 'block',
          textAlign: 'left',
          width: '100%',
          background: 'none',
          border: 'none',
          cursor: 'pointer',
          padding: '5px 0 5px 14px',
          position: 'relative',
          font: (yon ? 'var(--fw-semibold)' : 'var(--fw-regular)') + " 12px/1 var(--font-body)",
          letterSpacing: '0.04em',
          color: yon ? 'var(--qz-gold-deep)' : 'var(--qz-grey-500)',
          transition: 'color 160ms ease'
        }
      }, /*#__PURE__*/React.createElement("span", {
        style: {
          position: 'absolute',
          left: -1,
          top: '50%',
          transform: 'translateY(-50%)',
          width: yon ? 8 : 0,
          height: 2,
          background: 'var(--qz-gold)',
          transition: 'width 180ms ease'
        }
      }), y);
    }))));
  }));
}
function EventRowA({
  ev,
  first
}) {
  const [open, setOpen] = useStateA(false);
  const c = window.QZ_CATS[ev.cat];
  return /*#__PURE__*/React.createElement("div", {
    "data-year": ev.year,
    style: {
      position: 'relative',
      paddingLeft: 34
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      position: 'absolute',
      left: 5,
      top: first ? 22 : 0,
      bottom: 0,
      width: 1,
      background: 'var(--hairline)'
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      position: 'absolute',
      left: 0,
      top: 20,
      width: 11,
      height: 11,
      borderRadius: '50%',
      background: c.color,
      boxShadow: '0 0 0 4px var(--qz-warm-white)',
      zIndex: 1
    }
  }), /*#__PURE__*/React.createElement("button", {
    onClick: () => setOpen(!open),
    style: {
      display: 'grid',
      gridTemplateColumns: '78px 1fr auto',
      alignItems: 'baseline',
      gap: 'clamp(14px,2.2vw,34px)',
      width: '100%',
      textAlign: 'left',
      background: 'none',
      border: 'none',
      cursor: 'pointer',
      padding: '13px 0',
      borderTop: first ? 'none' : '1px solid var(--hairline)'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      font: "var(--fw-medium) 21px/1 var(--font-titling)",
      letterSpacing: '0.02em',
      color: 'var(--qz-charcoal)'
    }
  }, ev.year), /*#__PURE__*/React.createElement("span", {
    style: {
      minWidth: 0
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'block',
      lineHeight: 1.25
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'inline-flex',
      verticalAlign: '0.12em',
      marginRight: 14
    }
  }, /*#__PURE__*/React.createElement(CatTag, {
    cat: ev.cat
  })), /*#__PURE__*/React.createElement("span", {
    style: {
      font: "var(--fw-semibold) 20px/1.25 var(--font-display)",
      color: 'var(--text-primary)'
    }
  }, ev.title)), !open && /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'block',
      font: "var(--fw-regular) 15px/1.5 var(--font-body)",
      color: 'var(--text-muted)',
      margin: '5px 0 0',
      maxWidth: 620,
      overflow: 'hidden',
      textOverflow: 'ellipsis',
      whiteSpace: 'nowrap'
    }
  }, ev.text)), /*#__PURE__*/React.createElement("svg", {
    width: "15",
    height: "15",
    viewBox: "0 0 24 24",
    fill: "none",
    stroke: "var(--qz-grey-500)",
    strokeWidth: "2",
    style: {
      marginTop: 6,
      transform: open ? 'rotate(180deg)' : 'none',
      transition: 'transform 300ms ease',
      flexShrink: 0
    }
  }, /*#__PURE__*/React.createElement("path", {
    d: "m6 9 6 6 6-6"
  }))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateRows: open ? '1fr' : '0fr',
      transition: 'grid-template-rows 380ms cubic-bezier(.22,.61,.36,1)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      overflow: 'hidden'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: '78px 1fr',
      gap: 'clamp(14px,2.2vw,34px)',
      paddingBottom: 26
    }
  }, /*#__PURE__*/React.createElement("span", null), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: ev.img ? '1fr 190px' : '1fr',
      gap: 28,
      alignItems: 'start',
      maxWidth: ev.img ? 'none' : 640
    }
  }, /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement("p", {
    style: {
      font: "var(--fw-regular) 16px/1.62 var(--font-body)",
      color: 'var(--text-secondary)',
      margin: '0 0 12px'
    }
  }, ev.text), /*#__PURE__*/React.createElement("p", {
    style: {
      font: "var(--fw-regular) 15px/1.6 var(--font-body)",
      color: 'var(--text-secondary)',
      margin: 0,
      paddingTop: 12,
      borderTop: '1px solid var(--hairline)'
    }
  }, ev.more), /*#__PURE__*/React.createElement("a", {
    href: "#",
    onClick: e => e.preventDefault(),
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: 6,
      marginTop: 16,
      font: "var(--fw-semibold) 12px/1 var(--font-body)",
      letterSpacing: '0.08em',
      textTransform: 'uppercase',
      color: 'var(--link)',
      textDecoration: 'none'
    }
  }, "Read the story", /*#__PURE__*/React.createElement("svg", {
    width: "13",
    height: "13",
    viewBox: "0 0 24 24",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: "2"
  }, /*#__PURE__*/React.createElement("path", {
    d: "M5 12h14M13 6l6 6-6 6"
  })))), ev.img && /*#__PURE__*/React.createElement(EventPhoto, {
    img: ev.img,
    title: ev.title,
    ratio: "1 / 1"
  }))))));
}
function TimelineDecades() {
  const [filter, setFilter] = useStateA('all');
  const [activeDecade, setActiveDecade] = useStateA(window.QZ_DECADES[0]);
  const [activeYear, setActiveYear] = useStateA(null);
  const secRefs = useRefA({});
  const rootRef = useRefA(null);
  const all = window.QZ_TIMELINE;
  const events = filter === 'all' ? all : all.filter(e => e.cat === filter);

  // group by decade, with the sorted unique years present in each
  const groups = useMemoA(() => {
    return window.QZ_DECADES.map(d => {
      const items = events.filter(e => window.qzDecadeOf(e.year) === d);
      const years = [...new Set(items.map(e => e.year))].sort();
      return {
        d,
        items,
        years
      };
    }).filter(g => g.items.length);
  }, [filter]);
  const counts = {};
  groups.forEach(g => {
    counts[g.d] = g.items.length;
  });

  // track active decade + year as the reader scrolls
  useEffectA(() => {
    const decObs = new IntersectionObserver(es => {
      es.forEach(e => {
        if (e.isIntersecting) setActiveDecade(e.target.getAttribute('data-decade'));
      });
    }, {
      rootMargin: '-25% 0px -70% 0px'
    });
    Object.values(secRefs.current).forEach(el => el && decObs.observe(el));
    const rows = rootRef.current ? rootRef.current.querySelectorAll('[data-year]') : [];
    const yearObs = new IntersectionObserver(es => {
      es.forEach(e => {
        if (e.isIntersecting) setActiveYear(e.target.getAttribute('data-year'));
      });
    }, {
      rootMargin: '-30% 0px -65% 0px'
    });
    rows.forEach(el => yearObs.observe(el));
    return () => {
      decObs.disconnect();
      yearObs.disconnect();
    };
  }, [filter, groups.length]);
  const jumpDecade = d => {
    const el = secRefs.current[d];
    if (el) window.scrollTo({
      top: el.getBoundingClientRect().top + window.pageYOffset - 84,
      behavior: 'smooth'
    });
  };
  const jumpYear = y => {
    const el = rootRef.current && rootRef.current.querySelector('[data-year="' + y + '"]');
    if (el) window.scrollTo({
      top: el.getBoundingClientRect().top + window.pageYOffset - 96,
      behavior: 'smooth'
    });
  };
  return /*#__PURE__*/React.createElement("div", {
    style: {
      background: 'var(--qz-warm-white)',
      minHeight: '100vh'
    }
  }, /*#__PURE__*/React.createElement("header", {
    style: {
      position: 'relative',
      background: 'var(--qz-black)',
      overflow: 'hidden',
      padding: 'clamp(70px,9vw,120px) var(--gutter-lg) clamp(56px,7vw,90px)'
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: "../../assets/crest-white.png",
    alt: "",
    style: {
      position: 'absolute',
      top: '50%',
      right: '4%',
      transform: 'translateY(-50%)',
      width: 260,
      opacity: 0.06
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 1180,
      margin: '0 auto',
      position: 'relative'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      font: "var(--fw-semibold) 12px/1 var(--font-titling)",
      letterSpacing: '0.22em',
      textTransform: 'uppercase',
      color: 'var(--qz-gold)',
      marginBottom: 20
    }
  }, "Five decades \xB7 1970 \u2013 today"), /*#__PURE__*/React.createElement("h1", {
    style: {
      font: "var(--fw-regular) clamp(48px,7vw,88px)/1 var(--font-display)",
      letterSpacing: '-0.015em',
      color: '#fff',
      margin: '0 0 22px'
    }
  }, "The Queen Timeline"), /*#__PURE__*/React.createElement("p", {
    style: {
      font: "var(--fw-regular) 20px/1.55 var(--font-body)",
      color: 'rgba(255,255,255,0.78)',
      margin: 0,
      maxWidth: 620
    }
  }, "The story of the band, year by year \u2014 a guided path through the music, the performances and the moments that gathered a community."))), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'sticky',
      top: 0,
      zIndex: 20,
      background: 'rgba(247,246,243,0.92)',
      backdropFilter: 'saturate(180%) blur(10px)',
      borderBottom: '1px solid var(--hairline)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 1180,
      margin: '0 auto',
      padding: '14px var(--gutter-lg)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: 20,
      flexWrap: 'wrap'
    }
  }, /*#__PURE__*/React.createElement(FilterChips, {
    active: filter,
    onChange: setFilter
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      font: "var(--fw-medium) 12px/1 var(--font-body)",
      letterSpacing: '0.04em',
      color: 'var(--text-muted)'
    }
  }, events.length, " ", events.length === 1 ? 'event' : 'events'))), /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 1180,
      margin: '0 auto',
      padding: 'clamp(44px,5vw,72px) var(--gutter-lg) 140px',
      display: 'grid',
      gridTemplateColumns: '182px 1fr',
      gap: 'clamp(24px,4vw,72px)'
    }
  }, /*#__PURE__*/React.createElement(DecadeRail, {
    groups: groups,
    activeDecade: activeDecade,
    activeYear: activeYear,
    counts: counts,
    onJumpDecade: jumpDecade,
    onJumpYear: jumpYear
  }), /*#__PURE__*/React.createElement("div", {
    ref: rootRef
  }, groups.map(g => /*#__PURE__*/React.createElement("section", {
    key: g.d,
    "data-decade": g.d,
    ref: el => secRefs.current[g.d] = el,
    style: {
      marginBottom: 52
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'baseline',
      gap: 16,
      marginBottom: 20
    }
  }, /*#__PURE__*/React.createElement("h2", {
    style: {
      font: "var(--fw-medium) 38px/1 var(--font-display)",
      color: 'var(--qz-charcoal)',
      margin: 0
    }
  }, g.d), /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1,
      height: 1,
      background: 'var(--border-strong)'
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      font: "var(--fw-medium) 12px/1 var(--font-body)",
      letterSpacing: '0.04em',
      color: 'var(--text-muted)'
    }
  }, g.items.length)), /*#__PURE__*/React.createElement("div", null, g.items.map((ev, i) => /*#__PURE__*/React.createElement(EventRowA, {
    key: ev.year + ev.title,
    ev: ev,
    first: i === 0
  }))))), !groups.length && /*#__PURE__*/React.createElement("p", {
    style: {
      color: 'var(--text-muted)',
      font: 'var(--type-body)'
    }
  }, "No events in this category."))));
}
window.TimelineDecades = TimelineDecades;
})(); } catch (e) { __ds_ns.__errors.push({ path: "explorations/timeline/tl-variant-a.jsx", error: String((e && e.message) || e) }); }

// explorations/timeline/tl-variant-b.jsx
try { (() => {
// Direction B — "The Filmstrip": full-bleed dark horizontal scroll of event plates,
// with a decade scrubber, drag/wheel/keys, category dimming, and a click-to-expand overlay.
const {
  useState: useStateB,
  useEffect: useEffectB,
  useRef: useRefB
} = React;
function PlateB({
  ev,
  dim,
  onOpen,
  plateRef
}) {
  const c = window.QZ_CATS[ev.cat];
  return /*#__PURE__*/React.createElement("div", {
    ref: plateRef,
    style: {
      flex: '0 0 auto',
      width: 'min(78vw, 380px)',
      scrollSnapAlign: 'center',
      display: 'flex',
      flexDirection: 'column',
      opacity: dim ? 0.24 : 1,
      transition: 'opacity 300ms ease',
      paddingBottom: 8
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 12,
      marginBottom: 22
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      width: 13,
      height: 13,
      borderRadius: '50%',
      background: c.color,
      boxShadow: '0 0 0 5px rgba(255,255,255,0.06), 0 0 22px ' + c.color
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1,
      height: 1,
      background: 'rgba(255,255,255,0.12)'
    }
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      font: "var(--fw-regular) 64px/0.9 var(--font-display)",
      color: 'var(--qz-gold)',
      letterSpacing: '-0.01em',
      marginBottom: 18
    }
  }, ev.year), /*#__PURE__*/React.createElement("div", {
    style: {
      marginBottom: 18
    }
  }, /*#__PURE__*/React.createElement(CatTag, {
    cat: ev.cat,
    onDark: true
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      borderRadius: 3,
      overflow: 'hidden',
      border: '1px solid rgba(184,154,74,0.35)',
      marginBottom: 22
    }
  }, /*#__PURE__*/React.createElement(EventPhoto, {
    img: ev.img,
    title: ev.title,
    ratio: "5 / 4",
    rounded: 0
  })), /*#__PURE__*/React.createElement("h3", {
    style: {
      font: "var(--fw-semibold) 26px/1.15 var(--font-display)",
      color: '#fff',
      margin: '0 0 12px'
    }
  }, ev.title), /*#__PURE__*/React.createElement("p", {
    style: {
      font: "var(--fw-regular) 15px/1.6 var(--font-body)",
      color: 'rgba(255,255,255,0.72)',
      margin: '0 0 18px'
    }
  }, ev.text), /*#__PURE__*/React.createElement("button", {
    onClick: onOpen,
    style: {
      alignSelf: 'flex-start',
      background: 'none',
      border: 'none',
      cursor: 'pointer',
      padding: 0,
      display: 'inline-flex',
      alignItems: 'center',
      gap: 8,
      font: "var(--fw-semibold) 12px/1 var(--font-body)",
      letterSpacing: '0.1em',
      textTransform: 'uppercase',
      color: 'var(--qz-gold)'
    }
  }, "Open", /*#__PURE__*/React.createElement("svg", {
    width: "14",
    height: "14",
    viewBox: "0 0 24 24",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: "2"
  }, /*#__PURE__*/React.createElement("path", {
    d: "M7 17 17 7M9 7h8v8"
  }))));
}
function DetailOverlayB({
  ev,
  onClose
}) {
  useEffectB(() => {
    const k = e => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', k);
    return () => window.removeEventListener('keydown', k);
  }, [onClose]);
  if (!ev) return null;
  const c = window.QZ_CATS[ev.cat];
  return /*#__PURE__*/React.createElement("div", {
    onClick: onClose,
    style: {
      position: 'fixed',
      inset: 0,
      zIndex: 60,
      background: 'rgba(10,10,10,0.86)',
      backdropFilter: 'blur(6px)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      padding: 'clamp(20px,5vw,64px)',
      animation: 'qztlFade 260ms ease'
    }
  }, /*#__PURE__*/React.createElement("div", {
    onClick: e => e.stopPropagation(),
    style: {
      position: 'relative',
      width: 'min(880px, 100%)',
      background: '#141414',
      border: '1px solid rgba(184,154,74,0.4)',
      borderRadius: 4,
      overflow: 'hidden',
      display: 'grid',
      gridTemplateColumns: 'minmax(0,1fr) minmax(0,1.1fr)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      minHeight: 320
    }
  }, /*#__PURE__*/React.createElement(EventPhoto, {
    img: ev.img,
    title: ev.title,
    ratio: "auto",
    rounded: 0
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      padding: 'clamp(28px,3.5vw,48px)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 16,
      marginBottom: 20
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      font: "var(--fw-regular) 46px/1 var(--font-display)",
      color: 'var(--qz-gold)'
    }
  }, ev.year), /*#__PURE__*/React.createElement(CatTag, {
    cat: ev.cat,
    onDark: true
  })), /*#__PURE__*/React.createElement("h3", {
    style: {
      font: "var(--fw-semibold) 32px/1.12 var(--font-display)",
      color: '#fff',
      margin: '0 0 16px'
    }
  }, ev.title), /*#__PURE__*/React.createElement("p", {
    style: {
      font: "var(--fw-regular) 17px/1.62 var(--font-body)",
      color: 'rgba(255,255,255,0.82)',
      margin: '0 0 16px'
    }
  }, ev.text), /*#__PURE__*/React.createElement("p", {
    style: {
      font: "var(--fw-regular) 15px/1.6 var(--font-body)",
      color: 'rgba(255,255,255,0.6)',
      margin: '0 0 26px',
      paddingTop: 16,
      borderTop: '1px solid rgba(255,255,255,0.12)'
    }
  }, ev.more), /*#__PURE__*/React.createElement("a", {
    href: "#",
    onClick: e => e.preventDefault(),
    style: {
      font: "var(--fw-semibold) 12px/1 var(--font-body)",
      letterSpacing: '0.1em',
      textTransform: 'uppercase',
      color: 'var(--qz-gold)',
      textDecoration: 'none',
      display: 'inline-flex',
      alignItems: 'center',
      gap: 7
    }
  }, "Read the story", /*#__PURE__*/React.createElement("svg", {
    width: "14",
    height: "14",
    viewBox: "0 0 24 24",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: "2"
  }, /*#__PURE__*/React.createElement("path", {
    d: "M5 12h14M13 6l6 6-6 6"
  })))), /*#__PURE__*/React.createElement("button", {
    onClick: onClose,
    style: {
      position: 'absolute',
      top: 14,
      right: 14,
      width: 38,
      height: 38,
      borderRadius: '50%',
      background: 'rgba(0,0,0,0.4)',
      border: '1px solid rgba(255,255,255,0.2)',
      color: '#fff',
      cursor: 'pointer',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center'
    }
  }, /*#__PURE__*/React.createElement("svg", {
    width: "18",
    height: "18",
    viewBox: "0 0 24 24",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: "1.8"
  }, /*#__PURE__*/React.createElement("path", {
    d: "M18 6 6 18M6 6l12 12"
  })))));
}
function TimelineFilmstrip() {
  const [filter, setFilter] = useStateB('all');
  const [detail, setDetail] = useStateB(null);
  const [progress, setProgress] = useStateB(0);
  const [activeDecade, setActiveDecade] = useStateB(window.QZ_DECADES[0]);
  const trackRef = useRefB(null);
  const plateRefs = useRefB({});
  const all = window.QZ_TIMELINE;

  // wheel → horizontal; drag to pan
  useEffectB(() => {
    const el = trackRef.current;
    if (!el) return;
    const onWheel = e => {
      if (Math.abs(e.deltaY) > Math.abs(e.deltaX)) {
        el.scrollLeft += e.deltaY;
        e.preventDefault();
      }
    };
    let down = false,
      sx = 0,
      sl = 0;
    const md = e => {
      down = true;
      sx = e.pageX;
      sl = el.scrollLeft;
      el.style.cursor = 'grabbing';
    };
    const mm = e => {
      if (down) {
        el.scrollLeft = sl - (e.pageX - sx);
      }
    };
    const up = () => {
      down = false;
      el.style.cursor = 'grab';
    };
    const onScroll = () => {
      const max = el.scrollWidth - el.clientWidth;
      setProgress(max > 0 ? el.scrollLeft / max : 0);
      // nearest plate to centre → active decade (rect-based, offsetParent-safe)
      const tr = el.getBoundingClientRect();
      const mid = tr.left + el.clientWidth / 2;
      let best = null,
        bd = 1e9;
      Object.entries(plateRefs.current).forEach(([yr, node]) => {
        if (!node) return;
        const r = node.getBoundingClientRect();
        const cx = r.left + r.width / 2;
        if (Math.abs(cx - mid) < bd) {
          bd = Math.abs(cx - mid);
          best = yr;
        }
      });
      if (best) setActiveDecade(window.qzDecadeOf(best));
    };
    el.addEventListener('wheel', onWheel, {
      passive: false
    });
    el.addEventListener('mousedown', md);
    window.addEventListener('mousemove', mm);
    window.addEventListener('mouseup', up);
    el.addEventListener('scroll', onScroll, {
      passive: true
    });
    return () => {
      el.removeEventListener('wheel', onWheel);
      el.removeEventListener('mousedown', md);
      window.removeEventListener('mousemove', mm);
      window.removeEventListener('mouseup', up);
      el.removeEventListener('scroll', onScroll);
    };
  }, []);
  useEffectB(() => {
    const el = trackRef.current;
    const k = e => {
      if (e.key === 'ArrowRight') el.scrollBy({
        left: 380,
        behavior: 'smooth'
      });
      if (e.key === 'ArrowLeft') el.scrollBy({
        left: -380,
        behavior: 'smooth'
      });
    };
    window.addEventListener('keydown', k);
    return () => window.removeEventListener('keydown', k);
  }, []);
  const jumpDecade = d => {
    const first = all.find(e => window.qzDecadeOf(e.year) === d);
    const node = first && plateRefs.current[first.year];
    const el = trackRef.current;
    if (node && el) {
      const r = node.getBoundingClientRect(),
        tr = el.getBoundingClientRect();
      el.scrollTo({
        left: el.scrollLeft + (r.left - tr.left) - el.clientWidth / 2 + r.width / 2,
        behavior: 'smooth'
      });
    }
  };
  return /*#__PURE__*/React.createElement("div", {
    style: {
      background: 'var(--qz-black)',
      minHeight: '100vh',
      display: 'flex',
      flexDirection: 'column',
      position: 'relative'
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: "../../assets/crest-white.png",
    alt: "",
    style: {
      position: 'absolute',
      top: 40,
      right: 48,
      width: 130,
      opacity: 0.05,
      pointerEvents: 'none'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      padding: 'clamp(40px,5vw,64px) clamp(28px,5vw,72px) 0',
      flex: '0 0 auto'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      font: "var(--fw-semibold) 12px/1 var(--font-titling)",
      letterSpacing: '0.22em',
      textTransform: 'uppercase',
      color: 'var(--qz-gold)',
      marginBottom: 16
    }
  }, "Five decades \xB7 the filmstrip"), /*#__PURE__*/React.createElement("h1", {
    style: {
      font: "var(--fw-regular) clamp(40px,6vw,72px)/1 var(--font-display)",
      letterSpacing: '-0.015em',
      color: '#fff',
      margin: '0 0 22px'
    }
  }, "The Queen Timeline"), /*#__PURE__*/React.createElement(FilterChips, {
    active: filter,
    onChange: setFilter,
    onDark: true
  })), /*#__PURE__*/React.createElement("div", {
    ref: trackRef,
    style: {
      flex: 1,
      display: 'flex',
      alignItems: 'center',
      gap: 'clamp(28px,4vw,56px)',
      overflowX: 'auto',
      overflowY: 'hidden',
      padding: 'clamp(30px,4vw,52px) clamp(28px,5vw,72px)',
      cursor: 'grab',
      scrollSnapType: 'x proximity',
      scrollbarWidth: 'none'
    }
  }, all.map(ev => /*#__PURE__*/React.createElement(PlateB, {
    key: ev.year + ev.title,
    plateRef: el => plateRefs.current[ev.year] = el,
    ev: ev,
    dim: filter !== 'all' && ev.cat !== filter,
    onOpen: () => setDetail(ev)
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: '0 0 40px'
    }
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: '0 0 auto',
      padding: '0 clamp(28px,5vw,72px) clamp(28px,4vw,44px)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'relative',
      height: 2,
      background: 'rgba(255,255,255,0.14)',
      marginBottom: 18
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      left: 0,
      top: 0,
      height: 2,
      width: progress * 100 + '%',
      background: 'var(--qz-gold)',
      transition: 'width 80ms linear'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      top: '50%',
      left: progress * 100 + '%',
      transform: 'translate(-50%,-50%)',
      width: 11,
      height: 11,
      borderRadius: '50%',
      background: 'var(--qz-gold)',
      boxShadow: '0 0 12px var(--qz-gold)'
    }
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      justifyContent: 'space-between',
      gap: 8
    }
  }, window.QZ_DECADES.map(d => /*#__PURE__*/React.createElement("button", {
    key: d,
    onClick: () => jumpDecade(d),
    style: {
      background: 'none',
      border: 'none',
      cursor: 'pointer',
      padding: '4px 2px',
      font: "var(--fw-medium) 13px/1 var(--font-titling)",
      letterSpacing: '0.08em',
      color: activeDecade === d ? 'var(--qz-gold)' : 'rgba(255,255,255,0.4)',
      transition: 'color 220ms ease'
    }
  }, d))), /*#__PURE__*/React.createElement("div", {
    style: {
      marginTop: 16,
      font: "var(--fw-regular) 12px/1 var(--font-body)",
      letterSpacing: '0.04em',
      color: 'rgba(255,255,255,0.4)'
    }
  }, "Scroll, drag or use \u2190 \u2192 to move through the years \xB7 click a card to open")), /*#__PURE__*/React.createElement(DetailOverlayB, {
    ev: detail,
    onClose: () => setDetail(null)
  }));
}
window.TimelineFilmstrip = TimelineFilmstrip;
})(); } catch (e) { __ds_ns.__errors.push({ path: "explorations/timeline/tl-variant-b.jsx", error: String((e && e.message) || e) }); }

// ui_kits/website/App.jsx
try { (() => {
// Search overlay — full-screen, editorial, with quick suggestions.
function SearchOverlay({
  open,
  onClose
}) {
  const {
    Input,
    Tag
  } = window.QueenzoneDesignSystem_6c12e8;
  React.useEffect(() => {
    if (open) setTimeout(() => window.lucide && window.lucide.createIcons(), 30);
  }, [open]);
  if (!open) return null;
  const suggestions = ['Bohemian Rhapsody', 'Live Aid 1985', 'A Night at the Opera', 'Freddie Mercury', 'Wembley 1986', 'Brian May'];
  return /*#__PURE__*/React.createElement("div", {
    onClick: onClose,
    style: {
      position: 'fixed',
      inset: 0,
      zIndex: 100,
      background: 'rgba(17,17,17,0.72)',
      backdropFilter: 'blur(8px)',
      display: 'flex',
      alignItems: 'flex-start',
      justifyContent: 'center',
      paddingTop: '12vh',
      animation: 'qzFade 240ms ease'
    }
  }, /*#__PURE__*/React.createElement("div", {
    onClick: e => e.stopPropagation(),
    style: {
      width: 'min(720px, 90vw)',
      background: 'var(--qz-white)',
      borderRadius: 'var(--radius-md)',
      boxShadow: 'var(--shadow-lift)',
      padding: 36,
      position: 'relative'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 12,
      marginBottom: 8
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: "../../assets/crest-black.png",
    alt: "",
    style: {
      height: 30
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      font: 'var(--fw-semibold) 13px/1 var(--font-titling)',
      letterSpacing: '0.18em',
      textTransform: 'uppercase',
      color: 'var(--text-muted)'
    }
  }, "Search the Archive")), /*#__PURE__*/React.createElement(Input, {
    size: "lg",
    placeholder: "Search 4,000+ articles, photos and discussions\u2026",
    iconLeft: /*#__PURE__*/React.createElement("i", {
      "data-lucide": "search",
      style: {
        width: 20,
        height: 20
      }
    }),
    autoFocus: true,
    style: {
      fontSize: 18
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      marginTop: 26
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-medium) 12px/1 var(--font-body)',
      textTransform: 'uppercase',
      letterSpacing: '0.08em',
      color: 'var(--text-muted)',
      marginBottom: 14
    }
  }, "Popular searches"), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 9,
      flexWrap: 'wrap'
    }
  }, suggestions.map(s => /*#__PURE__*/React.createElement(Tag, {
    key: s,
    href: "#",
    onClick: e => e.preventDefault()
  }, s))))));
}

// Root app — routes between homepage, index pages and article reading view.
function App() {
  const [view, setView] = React.useState('home');
  const [story, setStory] = React.useState(null);
  const [search, setSearch] = React.useState(false);
  React.useEffect(() => {
    window.lucide && window.lucide.createIcons();
    const el = document.querySelector('.qz-scroll');
    if (el) el.scrollTop = 0;
  }, [view, search]);
  const openStory = s => {
    setStory(s);
    setView('article');
  };
  const go = page => setView(page);
  return /*#__PURE__*/React.createElement("div", {
    className: "qz-scroll",
    style: {
      height: '100vh',
      overflowY: 'auto',
      background: 'var(--qz-white)'
    }
  }, /*#__PURE__*/React.createElement(Header, {
    onSearch: () => setSearch(true),
    onHome: () => setView('home'),
    onNav: go,
    active: view,
    dark: true
  }), view === 'home' && /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement(Hero, {
    onOpen: () => openStory(window.QZ_DATA.featured[0]),
    onExplore: () => go('timeline')
  }), /*#__PURE__*/React.createElement(ExploreArchive, {
    onNav: go
  }), /*#__PURE__*/React.createElement(FeaturedStories, {
    onOpen: openStory
  }), /*#__PURE__*/React.createElement(Photography, null), /*#__PURE__*/React.createElement(ThisDay, null), /*#__PURE__*/React.createElement(Discussions, null), /*#__PURE__*/React.createElement(Restored, null), /*#__PURE__*/React.createElement(Timeline, null)), view === 'news' && /*#__PURE__*/React.createElement(NewsIndex, {
    onOpen: openStory
  }), view === 'stories' && /*#__PURE__*/React.createElement(StoriesIndex, {
    onOpen: openStory
  }), view === 'gallery' && /*#__PURE__*/React.createElement(PhotoGallery, null), view === 'timeline' && /*#__PURE__*/React.createElement(TimelinePage, null), view === 'forum' && /*#__PURE__*/React.createElement(ForumPage, {
    onOpenThread: () => openStory(window.QZ_DATA.featured[0])
  }), view === 'article' && /*#__PURE__*/React.createElement(ArticleView, {
    story: story,
    onBack: () => setView('home')
  }), /*#__PURE__*/React.createElement(Footer, null), /*#__PURE__*/React.createElement(SearchOverlay, {
    open: search,
    onClose: () => setSearch(false)
  }));
}
window.App = App;
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/website/App.jsx", error: String((e && e.message) || e) }); }

// ui_kits/website/ArticleView.jsx
try { (() => {
// Long-form article reading view — editorial measure, archival hero.
function ArticleView({
  story,
  onBack
}) {
  const {
    Badge,
    Tag,
    Button,
    IconButton
  } = window.QueenzoneDesignSystem_6c12e8;
  const s = story || window.QZ_DATA.featured[0];
  const body = ['It began, as these things often do, with low expectations. By the summer of 1985 the band had weathered a difficult few years — a patchy reception for recent records, and whispers that their finest moment had already passed.', 'What unfolded across twenty-one minutes at Wembley Stadium would settle the argument for a generation. From the opening bars, the performance was less a set than a conversation with ninety thousand people, every one of them held in the palm of a single hand.', 'The genius was in the restraint. Where others reached for spectacle, here was a band that understood the power of space — a held note, a raised fist, a chorus handed back to the crowd and sung straight back, word for word.', 'In the decades since, the footage has been studied frame by frame: the pacing, the song choices, the sheer economy of it all. It remains, by common consent, the high-water mark of the live stadium performance.'];
  return /*#__PURE__*/React.createElement("article", {
    style: {
      background: 'var(--qz-white)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'relative',
      height: 'min(56vh, 520px)',
      overflow: 'hidden',
      background: 'var(--qz-black)'
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: s.image,
    alt: "",
    style: {
      position: 'absolute',
      inset: 0,
      width: '100%',
      height: '100%',
      objectFit: 'cover',
      opacity: 0.78,
      filter: 'grayscale(1)'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      inset: 0,
      background: 'var(--scrim-bottom)'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      top: 24,
      left: 'max(24px, calc((100% - var(--container-text)) / 2))'
    }
  }, /*#__PURE__*/React.createElement(IconButton, {
    label: "Back",
    variant: "outline",
    onDark: true,
    onClick: onBack
  }, /*#__PURE__*/React.createElement("i", {
    "data-lucide": "arrow-left",
    style: {
      width: 18,
      height: 18
    }
  }))), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      bottom: 0,
      left: 0,
      right: 0,
      padding: '0 var(--gutter-lg) 48px'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 'var(--container-text)',
      margin: '0 auto'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      marginBottom: 18
    }
  }, /*#__PURE__*/React.createElement(Badge, {
    tone: "editorial",
    variant: "solid"
  }, s.category)), /*#__PURE__*/React.createElement("h1", {
    style: {
      font: 'var(--fw-medium) clamp(36px, 5vw, 60px)/1.03 var(--font-display)',
      letterSpacing: '-0.015em',
      color: 'var(--qz-white)',
      margin: 0
    }
  }, s.title)))), /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 'var(--container-text)',
      margin: '0 auto',
      padding: '48px var(--gutter-lg) 96px'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 16,
      paddingBottom: 28,
      marginBottom: 40,
      borderBottom: '1px solid var(--hairline)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      width: 42,
      height: 42,
      borderRadius: '50%',
      background: 'var(--qz-grey-200)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      font: 'var(--fw-semibold) 15px/1 var(--font-display)',
      color: 'var(--qz-charcoal)'
    }
  }, "QZ"), /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-semibold) 14px/1.3 var(--font-body)',
      color: 'var(--text-primary)'
    }
  }, "The Queenzone Archive"), /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-medium) 12px/1 var(--font-body)',
      textTransform: 'uppercase',
      letterSpacing: '0.05em',
      color: 'var(--text-muted)',
      marginTop: 3
    }
  }, s.meta)), /*#__PURE__*/React.createElement("div", {
    style: {
      marginLeft: 'auto',
      display: 'flex',
      gap: 6
    }
  }, /*#__PURE__*/React.createElement(IconButton, {
    label: "Bookmark",
    variant: "ghost"
  }, /*#__PURE__*/React.createElement("i", {
    "data-lucide": "bookmark",
    style: {
      width: 18,
      height: 18
    }
  })), /*#__PURE__*/React.createElement(IconButton, {
    label: "Share",
    variant: "ghost"
  }, /*#__PURE__*/React.createElement("i", {
    "data-lucide": "share-2",
    style: {
      width: 18,
      height: 18
    }
  })))), /*#__PURE__*/React.createElement("p", {
    style: {
      font: 'var(--fw-regular) 22px/1.6 var(--font-display)',
      color: 'var(--qz-charcoal)',
      marginBottom: 36
    }
  }, s.excerpt), /*#__PURE__*/React.createElement("div", {
    className: "qz-prose"
  }, body.map((para, i) => /*#__PURE__*/React.createElement("p", {
    key: i,
    style: {
      font: 'var(--fw-regular) 18px/1.75 var(--font-body)',
      color: 'var(--qz-grey-700)',
      margin: '0 0 26px'
    }
  }, i === 0 ? /*#__PURE__*/React.createElement("span", {
    style: {
      float: 'left',
      font: 'var(--fw-medium) 76px/0.78 var(--font-display)',
      color: 'var(--qz-charcoal)',
      margin: '6px 14px 0 0'
    }
  }, para[0]) : null, i === 0 ? para.slice(1) : para))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 8,
      flexWrap: 'wrap',
      marginTop: 40,
      paddingTop: 32,
      borderTop: '1px solid var(--hairline)'
    }
  }, /*#__PURE__*/React.createElement(Tag, {
    href: "#"
  }, "Live Aid"), /*#__PURE__*/React.createElement(Tag, {
    href: "#"
  }, "1985"), /*#__PURE__*/React.createElement(Tag, {
    href: "#"
  }, "Performance"), /*#__PURE__*/React.createElement(Tag, {
    href: "#"
  }, "Wembley"))));
}
window.ArticleView = ArticleView;
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/website/ArticleView.jsx", error: String((e && e.message) || e) }); }

// ui_kits/website/Footer.jsx
try { (() => {
// Site footer — crest seal, navigation columns, restrained.
function Footer() {
  const {
    Input,
    Button
  } = window.QueenzoneDesignSystem_6c12e8;
  const cols = [{
    h: 'Archive',
    links: ['News', 'Long-form Stories', 'Photography', 'Discography', 'Timeline']
  }, {
    h: 'Community',
    links: ['Forum', 'Members', 'Submit a Story', 'Restoration Project']
  }, {
    h: 'About',
    links: ['Our History', 'The Mission', 'Contact', 'Privacy']
  }];
  return /*#__PURE__*/React.createElement("footer", {
    style: {
      background: 'var(--qz-black)',
      borderTop: '1px solid var(--border-on-dark)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 'var(--container-max)',
      margin: '0 auto',
      padding: '72px var(--gutter-lg) 40px'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: '1.4fr 1fr 1fr 1fr',
      gap: 48,
      paddingBottom: 56,
      borderBottom: '1px solid var(--border-on-dark)'
    }
  }, /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 14,
      marginBottom: 22
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: "../../assets/crest-white.png",
    alt: "",
    style: {
      height: 44
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: 'var(--font-titling)',
      fontWeight: 600,
      fontSize: 18,
      letterSpacing: '0.18em',
      textTransform: 'uppercase',
      color: 'var(--qz-white)'
    }
  }, "Queenzone")), /*#__PURE__*/React.createElement("p", {
    style: {
      font: 'var(--fw-regular) 15px/1.6 var(--font-body)',
      color: 'rgba(255,255,255,0.6)',
      maxWidth: 320,
      marginBottom: 22
    }
  }, "The preserved archive of Queenzone.com \u2014 one of the longest-running independent Queen fan communities. Its news, stories, photography and forums, published at last."), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 8,
      maxWidth: 320
    }
  }, /*#__PURE__*/React.createElement(Input, {
    placeholder: "Email for archive updates",
    size: "sm",
    style: {
      background: 'rgba(255,255,255,0.06)',
      borderColor: 'var(--border-on-dark)',
      color: 'var(--qz-white)'
    }
  }), /*#__PURE__*/React.createElement(Button, {
    variant: "cta",
    size: "sm"
  }, "Join"))), cols.map(c => /*#__PURE__*/React.createElement("div", {
    key: c.h
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-semibold) 12px/1 var(--font-titling)',
      letterSpacing: '0.18em',
      textTransform: 'uppercase',
      color: 'var(--qz-gold)',
      marginBottom: 20
    }
  }, c.h), /*#__PURE__*/React.createElement("ul", {
    style: {
      listStyle: 'none',
      margin: 0,
      padding: 0,
      display: 'flex',
      flexDirection: 'column',
      gap: 13
    }
  }, c.links.map(l => /*#__PURE__*/React.createElement("li", {
    key: l
  }, /*#__PURE__*/React.createElement("a", {
    href: "#",
    onClick: e => e.preventDefault(),
    style: {
      font: 'var(--fw-regular) 15px/1 var(--font-body)',
      color: 'rgba(255,255,255,0.72)',
      textDecoration: 'none'
    },
    onMouseEnter: e => e.currentTarget.style.color = 'var(--qz-white)',
    onMouseLeave: e => e.currentTarget.style.color = 'rgba(255,255,255,0.72)'
  }, l))))))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      justifyContent: 'space-between',
      alignItems: 'center',
      paddingTop: 28,
      flexWrap: 'wrap',
      gap: 12
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      font: 'var(--fw-regular) 13px/1 var(--font-body)',
      color: 'rgba(255,255,255,0.42)'
    }
  }, "\xA9 2026 Queenzone.org \xB7 An independent fan archive. Not affiliated with Queen or its representatives."), /*#__PURE__*/React.createElement("span", {
    style: {
      font: 'var(--fw-regular) 13px/1 var(--font-body)',
      color: 'rgba(255,255,255,0.42)'
    }
  }, "Restored with care since 2001"))));
}
window.Footer = Footer;
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/website/Footer.jsx", error: String((e && e.message) || e) }); }

// ui_kits/website/Forum.jsx
try { (() => {
// Forum / Community page — boards index + recent threads, editorial-archival.
function ForumPage({
  onOpenThread
}) {
  const {
    Button,
    Tag,
    Badge,
    IconButton,
    Input
  } = window.QueenzoneDesignSystem_6c12e8;
  const stats = window.QZ_DATA.forumStats;
  const boards = window.QZ_DATA.forumBoards;
  const threads = window.QZ_DATA.forumThreads;
  const [tab, setTab] = React.useState('Latest');
  const tabs = ['Latest', 'Top', 'Unanswered'];
  return /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("section", {
    style: {
      background: 'var(--qz-black)',
      position: 'relative',
      overflow: 'hidden'
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: "../../assets/crest-white.png",
    alt: "",
    style: {
      position: 'absolute',
      right: -70,
      top: -60,
      width: 320,
      opacity: 0.06,
      pointerEvents: 'none'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 'var(--container-max)',
      margin: '0 auto',
      padding: '72px var(--gutter-lg) 56px',
      position: 'relative'
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "qz-eyebrow",
    style: {
      color: 'var(--qz-gold)',
      marginBottom: 18
    }
  }, "The Community"), /*#__PURE__*/React.createElement("h1", {
    style: {
      font: 'var(--fw-medium) clamp(40px, 5vw, 64px)/1.02 var(--font-display)',
      letterSpacing: '-0.015em',
      color: 'var(--qz-white)',
      margin: 0
    }
  }, "Forum"), /*#__PURE__*/React.createElement("p", {
    style: {
      font: 'var(--fw-regular) 19px/1.55 var(--font-body)',
      color: 'rgba(255,255,255,0.7)',
      margin: '20px 0 32px',
      maxWidth: 640
    }
  }, "The conversation that built Queenzone.com \u2014 more than 100,000 posts of news, debate and memory, preserved and open once more."), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 40,
      flexWrap: 'wrap'
    }
  }, [['members', stats.members], ['threads', stats.threads], ['posts', stats.posts]].map(([k, v]) => /*#__PURE__*/React.createElement("div", {
    key: k
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-semibold) 30px/1 var(--font-titling)',
      letterSpacing: '0.03em',
      color: 'var(--qz-gold)'
    }
  }, v), /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-medium) 12px/1 var(--font-body)',
      textTransform: 'uppercase',
      letterSpacing: '0.1em',
      color: 'rgba(255,255,255,0.5)',
      marginTop: 8
    }
  }, k)))))), /*#__PURE__*/React.createElement("section", {
    style: {
      maxWidth: 'var(--container-max)',
      margin: '0 auto',
      padding: '64px var(--gutter-lg) 32px'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'flex-end',
      justifyContent: 'space-between',
      gap: 24,
      borderBottom: '1px solid var(--hairline)',
      paddingBottom: 'var(--space-4)',
      marginBottom: 40
    }
  }, /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement("div", {
    className: "qz-eyebrow",
    style: {
      marginBottom: 12
    }
  }, "Discussion Boards"), /*#__PURE__*/React.createElement("h2", {
    style: {
      font: 'var(--type-h2)',
      margin: 0
    }
  }, "Browse the boards")), /*#__PURE__*/React.createElement(Button, {
    variant: "cta",
    size: "md",
    iconLeft: /*#__PURE__*/React.createElement("i", {
      "data-lucide": "pen-line",
      style: {
        width: 16,
        height: 16
      }
    })
  }, "New thread")), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: '1fr 1fr',
      gap: 0,
      border: '1px solid var(--border-default)',
      borderRadius: 'var(--radius-sm)',
      overflow: 'hidden'
    }
  }, boards.map((b, i) => {
    const col = i % 2;
    const row = Math.floor(i / 2);
    return /*#__PURE__*/React.createElement("a", {
      key: b.name,
      href: "#",
      onClick: e => e.preventDefault(),
      style: {
        display: 'flex',
        gap: 18,
        padding: '26px 28px',
        textDecoration: 'none',
        background: 'var(--qz-white)',
        borderTop: row > 0 ? '1px solid var(--border-default)' : 'none',
        borderLeft: col === 1 ? '1px solid var(--border-default)' : 'none',
        transition: 'background var(--dur-fast) var(--ease-out)'
      },
      onMouseEnter: e => {
        e.currentTarget.style.background = 'var(--qz-grey-50)';
      },
      onMouseLeave: e => {
        e.currentTarget.style.background = 'var(--qz-white)';
      }
    }, /*#__PURE__*/React.createElement("div", {
      style: {
        width: 46,
        height: 46,
        flexShrink: 0,
        borderRadius: 'var(--radius-sm)',
        background: 'var(--qz-warm-white)',
        border: '1px solid var(--border-default)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center'
      }
    }, /*#__PURE__*/React.createElement("i", {
      "data-lucide": b.icon,
      style: {
        width: 22,
        height: 22,
        color: 'var(--accent-archive)',
        strokeWidth: 1.5
      }
    })), /*#__PURE__*/React.createElement("div", {
      style: {
        minWidth: 0,
        flex: 1
      }
    }, /*#__PURE__*/React.createElement("h3", {
      style: {
        font: 'var(--fw-semibold) 20px/1.2 var(--font-display)',
        color: 'var(--text-primary)',
        margin: '0 0 5px'
      }
    }, b.name), /*#__PURE__*/React.createElement("p", {
      style: {
        font: 'var(--fw-regular) 14px/1.5 var(--font-body)',
        color: 'var(--text-secondary)',
        margin: '0 0 12px'
      }
    }, b.desc), /*#__PURE__*/React.createElement("div", {
      style: {
        display: 'flex',
        gap: 16,
        font: 'var(--fw-medium) 11.5px/1 var(--font-body)',
        textTransform: 'uppercase',
        letterSpacing: '0.06em',
        color: 'var(--text-muted)'
      }
    }, /*#__PURE__*/React.createElement("span", null, b.threads.toLocaleString(), " threads"), /*#__PURE__*/React.createElement("span", null, b.posts, " posts")), /*#__PURE__*/React.createElement("div", {
      style: {
        marginTop: 12,
        paddingTop: 12,
        borderTop: '1px solid var(--hairline)',
        font: 'var(--fw-regular) 12.5px/1.4 var(--font-body)',
        color: 'var(--text-muted)'
      }
    }, "Latest: ", /*#__PURE__*/React.createElement("span", {
      style: {
        color: 'var(--qz-charcoal)',
        fontWeight: 'var(--fw-medium)'
      }
    }, b.last.thread), " \xB7 ", b.last.when)));
  }))), /*#__PURE__*/React.createElement("section", {
    style: {
      maxWidth: 'var(--container-max)',
      margin: '0 auto',
      padding: '40px var(--gutter-lg) 96px'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: 20,
      flexWrap: 'wrap',
      marginBottom: 28
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 8
    }
  }, tabs.map(t => /*#__PURE__*/React.createElement(Tag, {
    key: t,
    href: "#",
    active: t === tab,
    onClick: e => {
      e.preventDefault();
      setTab(t);
    }
  }, t))), /*#__PURE__*/React.createElement("div", {
    style: {
      width: 260
    }
  }, /*#__PURE__*/React.createElement(Input, {
    size: "sm",
    placeholder: "Search discussions\u2026",
    iconLeft: /*#__PURE__*/React.createElement("i", {
      "data-lucide": "search",
      style: {
        width: 17,
        height: 17
      }
    })
  }))), /*#__PURE__*/React.createElement("div", {
    style: {
      border: '1px solid var(--border-default)',
      borderRadius: 'var(--radius-sm)',
      overflow: 'hidden'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: '1fr 110px 90px 110px',
      gap: 20,
      padding: '14px 24px',
      background: 'var(--qz-warm-white)',
      borderBottom: '1px solid var(--border-default)',
      font: 'var(--fw-semibold) 11px/1 var(--font-body)',
      textTransform: 'uppercase',
      letterSpacing: '0.08em',
      color: 'var(--text-muted)'
    }
  }, /*#__PURE__*/React.createElement("span", null, "Thread"), /*#__PURE__*/React.createElement("span", {
    style: {
      textAlign: 'right'
    }
  }, "Replies"), /*#__PURE__*/React.createElement("span", {
    style: {
      textAlign: 'right'
    }
  }, "Views"), /*#__PURE__*/React.createElement("span", {
    style: {
      textAlign: 'right'
    }
  }, "Activity")), threads.map((th, i) => /*#__PURE__*/React.createElement("a", {
    key: i,
    href: "#",
    onClick: e => {
      e.preventDefault();
      onOpenThread && onOpenThread(th);
    },
    style: {
      display: 'grid',
      gridTemplateColumns: '1fr 110px 90px 110px',
      gap: 20,
      alignItems: 'center',
      padding: '20px 24px',
      textDecoration: 'none',
      borderTop: i > 0 ? '1px solid var(--hairline)' : 'none',
      background: 'var(--qz-white)',
      transition: 'background var(--dur-fast) var(--ease-out)'
    },
    onMouseEnter: e => {
      e.currentTarget.style.background = 'var(--qz-grey-50)';
    },
    onMouseLeave: e => {
      e.currentTarget.style.background = 'var(--qz-white)';
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      minWidth: 0
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 10,
      marginBottom: 6
    }
  }, th.pinned && /*#__PURE__*/React.createElement("i", {
    "data-lucide": "pin",
    style: {
      width: 13,
      height: 13,
      color: 'var(--qz-gold-deep)'
    }
  }), /*#__PURE__*/React.createElement("h3", {
    style: {
      font: 'var(--fw-semibold) 17px/1.3 var(--font-display)',
      color: 'var(--text-primary)',
      margin: 0,
      overflow: 'hidden',
      textOverflow: 'ellipsis',
      whiteSpace: 'nowrap'
    }
  }, th.title)), /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-medium) 11.5px/1 var(--font-body)',
      textTransform: 'uppercase',
      letterSpacing: '0.05em',
      color: 'var(--text-muted)'
    }
  }, th.board, " \xB7 by ", th.author)), /*#__PURE__*/React.createElement("span", {
    style: {
      textAlign: 'right',
      font: 'var(--fw-semibold) 16px/1 var(--font-body)',
      color: 'var(--qz-charcoal)'
    }
  }, th.replies.toLocaleString()), /*#__PURE__*/React.createElement("span", {
    style: {
      textAlign: 'right',
      font: 'var(--fw-regular) 14px/1 var(--font-body)',
      color: 'var(--text-muted)'
    }
  }, th.views), /*#__PURE__*/React.createElement("span", {
    style: {
      textAlign: 'right',
      font: 'var(--fw-medium) 12px/1 var(--font-body)',
      textTransform: 'uppercase',
      letterSpacing: '0.05em',
      color: 'var(--text-muted)'
    }
  }, th.when))))));
}
window.ForumPage = ForumPage;
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/website/Forum.jsx", error: String((e && e.message) || e) }); }

// ui_kits/website/Header.jsx
try { (() => {
// Queenzone site header — crest mark, editorial wordmark, quiet nav.
// `dark` renders the inverted masthead: rich-black background, white type.
function Header({
  onSearch,
  onHome,
  onNav,
  active,
  dark = false
}) {
  const {
    IconButton,
    Button
  } = window.QueenzoneDesignSystem_6c12e8;
  const [scrolled, setScrolled] = React.useState(false);
  React.useEffect(() => {
    const el = document.querySelector('.qz-scroll');
    const onScroll = () => setScrolled((el ? el.scrollTop : window.scrollY) > 12);
    const target = el || window;
    target.addEventListener('scroll', onScroll);
    return () => target.removeEventListener('scroll', onScroll);
  }, []);
  const nav = [{
    label: 'News',
    page: 'news'
  }, {
    label: 'Stories',
    page: 'stories'
  }, {
    label: 'Photography',
    page: 'gallery'
  }, {
    label: 'Forum',
    page: 'forum'
  }, {
    label: 'Timeline',
    page: 'timeline'
  }];

  // Tone tokens
  const bg = dark ? scrolled ? 'rgba(17,17,17,0.92)' : 'var(--qz-black)' : scrolled ? 'rgba(255,255,255,0.92)' : 'var(--qz-white)';
  const wordmark = dark ? 'var(--qz-white)' : 'var(--qz-charcoal)';
  const navIdle = dark ? 'rgba(255,255,255,0.82)' : 'var(--qz-charcoal)';
  const accent = dark ? 'var(--qz-gold)' : 'var(--qz-blue)';
  const crest = dark ? '../../assets/crest-white.png' : '../../assets/crest-black.png';
  return /*#__PURE__*/React.createElement("header", {
    style: {
      position: 'sticky',
      top: 0,
      zIndex: 50,
      background: bg,
      backdropFilter: scrolled ? 'saturate(180%) blur(12px)' : 'none',
      // Gilt hairline — the brand's antique-gold "key highlight", as a single editorial rule
      // separating the masthead from the content beneath it. Soft shadow on scroll for depth.
      borderBottom: '1px solid rgba(184,154,74,0.55)',
      boxShadow: scrolled ? dark ? '0 8px 28px rgba(0,0,0,0.45)' : '0 6px 22px rgba(17,17,17,0.10)' : 'none',
      transition: 'background var(--dur-base) var(--ease-out), box-shadow var(--dur-base) var(--ease-out)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 'var(--container-max)',
      margin: '0 auto',
      padding: '0 var(--gutter-lg)',
      height: 76,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: 24
    }
  }, /*#__PURE__*/React.createElement("a", {
    href: "#",
    onClick: e => {
      e.preventDefault();
      onHome && onHome();
    },
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 14,
      textDecoration: 'none'
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: crest,
    alt: "Queen crest",
    style: {
      height: 42,
      width: 'auto'
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: 'var(--font-titling)',
      fontWeight: 600,
      fontSize: 21,
      letterSpacing: '0.18em',
      textTransform: 'uppercase',
      color: wordmark
    }
  }, "Queenzone")), /*#__PURE__*/React.createElement("nav", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 30
    }
  }, nav.map(n => {
    const isActive = active === n.page && n.page !== 'home';
    return /*#__PURE__*/React.createElement("a", {
      key: n.label,
      href: "#",
      onClick: e => {
        e.preventDefault();
        onNav && onNav(n.page);
      },
      style: {
        font: 'var(--fw-medium) 14px/1 var(--font-body)',
        letterSpacing: '0.03em',
        color: isActive ? accent : navIdle,
        textDecoration: 'none',
        paddingBottom: 2,
        borderBottom: isActive ? `2px solid ${accent}` : '2px solid transparent',
        transition: 'color var(--dur-fast) var(--ease-out)'
      }
    }, n.label);
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 10
    }
  }, /*#__PURE__*/React.createElement(IconButton, {
    label: "Search",
    variant: "ghost",
    onDark: dark,
    onClick: onSearch
  }, /*#__PURE__*/React.createElement("i", {
    "data-lucide": "search",
    style: {
      width: 19,
      height: 19
    }
  })), /*#__PURE__*/React.createElement(Button, {
    variant: "secondary",
    size: "sm",
    style: dark ? {
      color: 'var(--qz-white)',
      borderColor: 'var(--border-on-dark)'
    } : {}
  }, "Sign in"))));
}
window.Header = Header;
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/website/Header.jsx", error: String((e && e.message) || e) }); }

// ui_kits/website/Hero.jsx
try { (() => {
// Homepage hero — a living archive: the previous eras of Queenzone.com
// morph across a floating "site window", reinforcing that this is the
// restored home of two decades of Queen community history.
const QZ_ERAS = [{
  year: '1999',
  img: '../../assets/eras/queenzone-1999.png',
  label: 'The Queen Internet Zone',
  glow: '#c81e2e'
}, {
  year: '2000',
  img: '../../assets/eras/queenzone-2000.png',
  label: 'Queen Internet Zone',
  glow: '#3c4a5a'
}, {
  year: '2002',
  img: '../../assets/eras/queenzone-2002.png',
  label: 'www.queenzone.com',
  glow: '#9c1414'
}, {
  year: '2004',
  img: '../../assets/eras/queenzone-2004.png',
  label: 'Queenzone.com',
  glow: '#1668ad'
}, {
  year: '2020',
  img: '../../assets/eras/queenzone-2020.png',
  label: 'QUEENZONE.COM',
  glow: '#8b95a1'
}];
function Hero({
  onOpen,
  onExplore
}) {
  const {
    Button,
    Badge
  } = window.QueenzoneDesignSystem_6c12e8;
  const h = window.QZ_DATA.hero;
  const [i, setI] = React.useState(0);
  React.useEffect(() => {
    const t = setInterval(() => setI(n => (n + 1) % QZ_ERAS.length), 3600);
    return () => clearInterval(t);
  }, []);
  const active = QZ_ERAS[i];
  return /*#__PURE__*/React.createElement("section", {
    style: {
      position: 'relative',
      minHeight: 'min(84vh, 760px)',
      display: 'flex',
      alignItems: 'center',
      overflow: 'hidden',
      background: 'var(--qz-black)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      top: '-10%',
      right: '-6%',
      width: '70%',
      height: '120%',
      pointerEvents: 'none',
      background: 'radial-gradient(closest-side, ' + active.glow + '55, transparent 72%)',
      transition: 'background 900ms var(--ease-out)',
      filter: 'blur(8px)'
    }
  }), /*#__PURE__*/React.createElement("img", {
    src: "../../assets/crest-white.png",
    alt: "",
    style: {
      position: 'absolute',
      top: 40,
      right: 48,
      width: 120,
      opacity: 0.08,
      pointerEvents: 'none'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'relative',
      width: '100%',
      maxWidth: 'var(--container-max)',
      margin: '0 auto',
      padding: '0 var(--gutter-lg)',
      display: 'grid',
      gridTemplateColumns: 'minmax(0, 1fr) minmax(0, 1.05fr)',
      gap: 'clamp(32px, 5vw, 80px)',
      alignItems: 'center'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 560
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      marginBottom: 22
    }
  }, /*#__PURE__*/React.createElement(Badge, {
    tone: "editorial",
    variant: "solid"
  }, h.category)), /*#__PURE__*/React.createElement("h1", {
    style: {
      font: 'var(--fw-medium) clamp(40px, 5vw, 68px)/1.03 var(--font-display)',
      letterSpacing: '-0.015em',
      color: 'var(--qz-white)',
      margin: '0 0 22px'
    }
  }, h.title), /*#__PURE__*/React.createElement("p", {
    style: {
      font: 'var(--fw-regular) 19px/1.55 var(--font-body)',
      color: 'rgba(255,255,255,0.82)',
      margin: '0 0 30px',
      maxWidth: 500
    }
  }, h.standfirst), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 22,
      flexWrap: 'wrap'
    }
  }, /*#__PURE__*/React.createElement(Button, {
    variant: "cta",
    size: "lg",
    onClick: onExplore || onOpen
  }, "Explore the timeline"), /*#__PURE__*/React.createElement("span", {
    style: {
      font: 'var(--fw-medium) 13px/1 var(--font-body)',
      textTransform: 'uppercase',
      letterSpacing: '0.08em',
      color: 'rgba(255,255,255,0.55)'
    }
  }, h.meta))), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'relative',
      width: '100%',
      aspectRatio: '4 / 3',
      maxHeight: 520
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      inset: '18px -22px -20px 26px',
      border: '1px solid rgba(255,255,255,0.09)',
      borderRadius: 10,
      transform: 'rotate(2.2deg)',
      background: 'rgba(255,255,255,0.02)'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      inset: '9px -11px -10px 13px',
      border: '1px solid rgba(255,255,255,0.12)',
      borderRadius: 10,
      transform: 'rotate(1deg)',
      background: 'rgba(255,255,255,0.03)'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      inset: 0,
      borderRadius: 10,
      overflow: 'hidden',
      border: '1px solid rgba(184,154,74,0.5)',
      boxShadow: '0 40px 90px rgba(0,0,0,0.6)',
      background: '#0d0d0d'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'relative',
      zIndex: 3,
      height: 38,
      display: 'flex',
      alignItems: 'center',
      gap: 8,
      padding: '0 14px',
      background: '#171717',
      borderBottom: '1px solid rgba(184,154,74,0.35)'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      width: 9,
      height: 9,
      borderRadius: '50%',
      background: '#3a3a3a'
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      width: 9,
      height: 9,
      borderRadius: '50%',
      background: '#3a3a3a'
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      width: 9,
      height: 9,
      borderRadius: '50%',
      background: '#3a3a3a'
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      marginLeft: 10,
      font: 'var(--fw-semibold) 11px/1 var(--font-titling)',
      letterSpacing: '0.14em',
      textTransform: 'uppercase',
      color: 'rgba(255,255,255,0.6)',
      transition: 'color 500ms'
    }
  }, active.label), /*#__PURE__*/React.createElement("span", {
    style: {
      marginLeft: 'auto',
      font: 'var(--fw-medium) 12px/1 var(--font-titling)',
      letterSpacing: '0.12em',
      color: 'var(--qz-gold)'
    }
  }, active.year)), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      top: 38,
      left: 0,
      right: 0,
      bottom: 0,
      overflow: 'hidden'
    }
  }, /*#__PURE__*/React.createElement("img", {
    key: active.year,
    src: active.img,
    alt: 'Queenzone.com in ' + active.year,
    style: {
      position: 'absolute',
      inset: 0,
      width: '100%',
      height: '100%',
      objectFit: 'cover',
      objectPosition: 'top center',
      opacity: 1
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      inset: 0,
      pointerEvents: 'none',
      background: 'linear-gradient(180deg, transparent 55%, rgba(13,13,13,0.35))',
      backgroundImage: 'repeating-linear-gradient(0deg, rgba(0,0,0,0.14) 0 1px, transparent 1px 3px)'
    }
  }))), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      left: 0,
      bottom: -46,
      display: 'flex',
      alignItems: 'center',
      gap: 16
    }
  }, QZ_ERAS.map((e, n) => /*#__PURE__*/React.createElement("button", {
    key: e.year,
    onClick: () => setI(n),
    style: {
      background: 'none',
      border: 'none',
      cursor: 'pointer',
      padding: '4px 0',
      font: 'var(--fw-medium) 12px/1 var(--font-titling)',
      letterSpacing: '0.14em',
      color: n === i ? 'var(--qz-gold)' : 'rgba(255,255,255,0.4)',
      transition: 'color 300ms',
      position: 'relative'
    }
  }, e.year, /*#__PURE__*/React.createElement("span", {
    style: {
      position: 'absolute',
      left: 0,
      right: 0,
      bottom: -6,
      height: 2,
      background: 'var(--qz-gold)',
      borderRadius: 2,
      opacity: n === i ? 1 : 0,
      transition: 'opacity 300ms'
    }
  })))))));
}
window.Hero = Hero;
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/website/Hero.jsx", error: String((e && e.message) || e) }); }

// ui_kits/website/MobileScreens.jsx
try { (() => {
// Queenzone mobile screens — rendered inside IOSDevice frames.
// Mobile-first: the primary device per the brief.

function MHeader({
  dark
}) {
  const bg = dark ? 'transparent' : 'rgba(255,255,255,0.9)';
  const fg = dark ? 'var(--qz-white)' : 'var(--qz-charcoal)';
  const crest = dark ? '../../assets/crest-white.png' : '../../assets/crest-black.png';
  return /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'sticky',
      top: 0,
      zIndex: 30,
      background: bg,
      backdropFilter: dark ? 'none' : 'saturate(180%) blur(10px)',
      borderBottom: dark ? 'none' : '1px solid var(--hairline)',
      padding: '52px 18px 14px',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 9
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: crest,
    alt: "",
    style: {
      height: 26
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: 'var(--font-titling)',
      fontWeight: 600,
      fontSize: 14,
      letterSpacing: '0.16em',
      textTransform: 'uppercase',
      color: fg
    }
  }, "Queenzone")), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 14,
      color: fg
    }
  }, /*#__PURE__*/React.createElement("i", {
    "data-lucide": "search",
    style: {
      width: 20,
      height: 20
    }
  }), /*#__PURE__*/React.createElement("i", {
    "data-lucide": "menu",
    style: {
      width: 20,
      height: 20
    }
  })));
}
function MChips({
  items,
  dark
}) {
  const [a, setA] = React.useState(items[0]);
  const {
    Tag
  } = window.QueenzoneDesignSystem_6c12e8;
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 8,
      overflowX: 'auto',
      padding: '0 18px 4px',
      WebkitOverflowScrolling: 'touch'
    }
  }, items.map(t => /*#__PURE__*/React.createElement("div", {
    key: t,
    style: {
      flex: '0 0 auto'
    }
  }, /*#__PURE__*/React.createElement(Tag, {
    href: "#",
    onDark: dark,
    active: t === a,
    onClick: e => {
      e.preventDefault();
      setA(t);
    }
  }, t))));
}

// 1 — Home
function MobileHome() {
  const {
    Badge,
    Button
  } = window.QueenzoneDesignSystem_6c12e8;
  const h = window.QZ_DATA.hero;
  const ex = window.QZ_DATA.explore;
  const td = window.QZ_DATA.thisDay[0];
  return /*#__PURE__*/React.createElement("div", {
    style: {
      background: 'var(--qz-white)',
      minHeight: '100%'
    }
  }, /*#__PURE__*/React.createElement(MHeader, {
    dark: true
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      marginTop: -88
    }
  }, /*#__PURE__*/React.createElement("section", {
    style: {
      position: 'relative',
      minHeight: 500,
      display: 'flex',
      alignItems: 'flex-end',
      overflow: 'hidden',
      background: 'var(--qz-black)'
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: h.image,
    alt: "",
    style: {
      position: 'absolute',
      inset: 0,
      width: '100%',
      height: '100%',
      objectFit: 'cover',
      opacity: 0.82
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      inset: 0,
      background: 'var(--scrim-bottom)'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'relative',
      padding: '0 20px 30px'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      marginBottom: 14
    }
  }, /*#__PURE__*/React.createElement(Badge, {
    tone: "editorial",
    variant: "solid"
  }, h.category)), /*#__PURE__*/React.createElement("h1", {
    style: {
      font: 'var(--fw-medium) 34px/1.05 var(--font-display)',
      letterSpacing: '-0.015em',
      color: 'var(--qz-white)',
      margin: '0 0 12px'
    }
  }, h.title), /*#__PURE__*/React.createElement("p", {
    style: {
      font: 'var(--fw-regular) 15px/1.5 var(--font-body)',
      color: 'rgba(255,255,255,0.82)',
      margin: '0 0 18px'
    }
  }, h.standfirst), /*#__PURE__*/React.createElement(Button, {
    variant: "cta",
    size: "md",
    fullWidth: true
  }, "Read the story")))), /*#__PURE__*/React.createElement("section", {
    style: {
      padding: '36px 20px',
      background: 'var(--qz-warm-white)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "qz-eyebrow",
    style: {
      color: 'var(--accent-archive)',
      marginBottom: 8
    }
  }, "The Queenzone.com Archive"), /*#__PURE__*/React.createElement("h2", {
    style: {
      font: 'var(--fw-medium) 26px/1.1 var(--font-display)',
      margin: '0 0 20px'
    }
  }, "Explore the Archive"), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: '1fr 1fr',
      gap: 12
    }
  }, ex.map(it => /*#__PURE__*/React.createElement("div", {
    key: it.label,
    style: {
      padding: '18px 16px',
      background: 'var(--qz-white)',
      border: '1px solid var(--border-default)',
      borderRadius: 'var(--radius-sm)'
    }
  }, /*#__PURE__*/React.createElement("i", {
    "data-lucide": it.icon,
    style: {
      width: 22,
      height: 22,
      color: 'var(--qz-charcoal)',
      strokeWidth: 1.4
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-semibold) 15px/1.25 var(--font-display)',
      marginTop: 12,
      color: 'var(--text-primary)'
    }
  }, it.label), /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-medium) 11px/1 var(--font-body)',
      textTransform: 'uppercase',
      letterSpacing: '0.05em',
      color: 'var(--text-muted)',
      marginTop: 5
    }
  }, it.count))))), /*#__PURE__*/React.createElement("section", {
    style: {
      padding: '36px 20px',
      background: 'var(--qz-black)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-semibold) 12px/1 var(--font-titling)',
      letterSpacing: '0.18em',
      textTransform: 'uppercase',
      color: 'var(--qz-gold)',
      marginBottom: 10
    }
  }, "On This Day"), /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-semibold) 13px/1 var(--font-titling)',
      letterSpacing: '0.12em',
      textTransform: 'uppercase',
      color: 'rgba(255,255,255,0.55)',
      marginBottom: 14
    }
  }, td.date), /*#__PURE__*/React.createElement("p", {
    style: {
      font: 'var(--fw-regular) 19px/1.45 var(--font-display)',
      color: 'var(--qz-white)',
      margin: 0
    }
  }, td.text)));
}

// 2 — News list
function MobileNews() {
  const news = window.QZ_DATA.news;
  return /*#__PURE__*/React.createElement("div", {
    style: {
      background: 'var(--qz-white)',
      minHeight: '100%'
    }
  }, /*#__PURE__*/React.createElement(MHeader, null), /*#__PURE__*/React.createElement("section", {
    style: {
      background: 'var(--qz-black)',
      padding: '28px 20px 26px',
      position: 'relative',
      overflow: 'hidden'
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: "../../assets/crest-white.png",
    alt: "",
    style: {
      position: 'absolute',
      right: -40,
      top: -20,
      width: 150,
      opacity: 0.06
    }
  }), /*#__PURE__*/React.createElement("div", {
    className: "qz-eyebrow",
    style: {
      color: 'var(--qz-gold)',
      marginBottom: 10
    }
  }, "The Archive"), /*#__PURE__*/React.createElement("h1", {
    style: {
      font: 'var(--fw-medium) 40px/1 var(--font-display)',
      color: 'var(--qz-white)',
      margin: 0
    }
  }, "News"), /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-medium) 12px/1 var(--font-body)',
      textTransform: 'uppercase',
      letterSpacing: '0.1em',
      color: 'rgba(255,255,255,0.45)',
      marginTop: 14
    }
  }, "4,000+ articles")), /*#__PURE__*/React.createElement("div", {
    style: {
      padding: '18px 0 8px'
    }
  }, /*#__PURE__*/React.createElement(MChips, {
    items: window.QZ_DATA.newsYears
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      padding: '0 20px 30px'
    }
  }, news.map((n, i) => /*#__PURE__*/React.createElement("a", {
    key: i,
    href: "#",
    onClick: e => e.preventDefault(),
    style: {
      display: 'block',
      textDecoration: 'none',
      padding: '20px 0',
      borderBottom: '1px solid var(--hairline)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 12,
      marginBottom: 8
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      font: 'var(--fw-semibold) 12px/1 var(--font-titling)',
      letterSpacing: '0.1em',
      textTransform: 'uppercase',
      color: 'var(--accent-archive)'
    }
  }, n.date), /*#__PURE__*/React.createElement("span", {
    style: {
      font: 'var(--fw-medium) 11px/1 var(--font-body)',
      letterSpacing: '0.08em',
      textTransform: 'uppercase',
      color: 'var(--text-muted)'
    }
  }, n.cat)), /*#__PURE__*/React.createElement("h3", {
    style: {
      font: 'var(--fw-semibold) 20px/1.25 var(--font-display)',
      color: 'var(--text-primary)',
      margin: '0 0 5px'
    }
  }, n.title), /*#__PURE__*/React.createElement("p", {
    style: {
      font: 'var(--fw-regular) 14px/1.5 var(--font-body)',
      color: 'var(--text-secondary)',
      margin: 0
    }
  }, n.excerpt)))));
}

// 3 — Photo gallery
function MobileGallery() {
  const photos = window.QZ_DATA.gallery;
  return /*#__PURE__*/React.createElement("div", {
    style: {
      background: 'var(--qz-white)',
      minHeight: '100%'
    }
  }, /*#__PURE__*/React.createElement(MHeader, null), /*#__PURE__*/React.createElement("section", {
    style: {
      background: 'var(--qz-black)',
      padding: '28px 20px 26px'
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "qz-eyebrow",
    style: {
      color: 'var(--qz-gold)',
      marginBottom: 10
    }
  }, "The Photographic Archive"), /*#__PURE__*/React.createElement("h1", {
    style: {
      font: 'var(--fw-medium) 40px/1 var(--font-display)',
      color: 'var(--qz-white)',
      margin: 0
    }
  }, "Photography")), /*#__PURE__*/React.createElement("div", {
    style: {
      padding: '18px 0 14px'
    }
  }, /*#__PURE__*/React.createElement(MChips, {
    items: window.QZ_DATA.galleryFilters
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: '1fr 1fr',
      gap: 8,
      padding: '0 18px 28px'
    }
  }, photos.slice(0, 8).map((p, i) => /*#__PURE__*/React.createElement("figure", {
    key: i,
    style: {
      margin: 0,
      position: 'relative',
      aspectRatio: '1',
      overflow: 'hidden',
      borderRadius: 'var(--radius-sm)',
      background: 'var(--qz-grey-200)'
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: p.image,
    alt: p.caption,
    style: {
      width: '100%',
      height: '100%',
      objectFit: 'cover',
      filter: 'grayscale(1)'
    }
  }), /*#__PURE__*/React.createElement("figcaption", {
    style: {
      position: 'absolute',
      left: 0,
      right: 0,
      bottom: 0,
      padding: '20px 10px 8px',
      background: 'var(--scrim-soft)',
      font: 'var(--fw-medium) 10px/1.3 var(--font-body)',
      textTransform: 'uppercase',
      letterSpacing: '0.05em',
      color: 'rgba(255,255,255,0.92)',
      display: 'flex',
      justifyContent: 'space-between'
    }
  }, /*#__PURE__*/React.createElement("span", null, p.caption), /*#__PURE__*/React.createElement("span", {
    style: {
      color: 'var(--qz-gold)'
    }
  }, p.year))))));
}

// 4 — Article reading view
function MobileArticle() {
  const {
    Badge,
    Tag
  } = window.QueenzoneDesignSystem_6c12e8;
  const s = window.QZ_DATA.featured[0];
  const body = ['It began, as these things often do, with low expectations. By the summer of 1985 the band had weathered a difficult few years, and whispers that their finest moment had passed.', 'What unfolded across twenty-one minutes at Wembley would settle the argument for a generation — less a set than a conversation with ninety thousand people, every one held in the palm of a single hand.'];
  return /*#__PURE__*/React.createElement("div", {
    style: {
      background: 'var(--qz-white)',
      minHeight: '100%'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'relative',
      height: 340,
      overflow: 'hidden',
      background: 'var(--qz-black)'
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: s.image,
    alt: "",
    style: {
      position: 'absolute',
      inset: 0,
      width: '100%',
      height: '100%',
      objectFit: 'cover',
      opacity: 0.78,
      filter: 'grayscale(1)'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      inset: 0,
      background: 'var(--scrim-bottom)'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      top: 52,
      left: 18
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      width: 38,
      height: 38,
      borderRadius: '50%',
      background: 'rgba(255,255,255,0.14)',
      border: '1px solid rgba(255,255,255,0.3)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center'
    }
  }, /*#__PURE__*/React.createElement("i", {
    "data-lucide": "arrow-left",
    style: {
      width: 18,
      height: 18,
      color: '#fff'
    }
  }))), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      bottom: 0,
      padding: '0 20px 24px'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      marginBottom: 12
    }
  }, /*#__PURE__*/React.createElement(Badge, {
    tone: "editorial",
    variant: "solid"
  }, s.category)), /*#__PURE__*/React.createElement("h1", {
    style: {
      font: 'var(--fw-medium) 30px/1.08 var(--font-display)',
      letterSpacing: '-0.015em',
      color: 'var(--qz-white)',
      margin: 0
    }
  }, s.title))), /*#__PURE__*/React.createElement("div", {
    style: {
      padding: '24px 20px 40px'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 12,
      paddingBottom: 18,
      marginBottom: 22,
      borderBottom: '1px solid var(--hairline)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      width: 36,
      height: 36,
      borderRadius: '50%',
      background: 'var(--qz-grey-200)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      font: 'var(--fw-semibold) 13px/1 var(--font-display)'
    }
  }, "QZ"), /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-semibold) 13px/1.3 var(--font-body)',
      color: 'var(--text-primary)'
    }
  }, "The Queenzone Archive"), /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-medium) 11px/1 var(--font-body)',
      textTransform: 'uppercase',
      letterSpacing: '0.05em',
      color: 'var(--text-muted)',
      marginTop: 3
    }
  }, s.meta))), /*#__PURE__*/React.createElement("p", {
    style: {
      font: 'var(--fw-regular) 18px/1.55 var(--font-display)',
      color: 'var(--qz-charcoal)',
      margin: '0 0 22px'
    }
  }, s.excerpt), body.map((p, i) => /*#__PURE__*/React.createElement("p", {
    key: i,
    style: {
      font: 'var(--fw-regular) 16px/1.7 var(--font-body)',
      color: 'var(--qz-grey-700)',
      margin: '0 0 20px'
    }
  }, i === 0 ? /*#__PURE__*/React.createElement("span", {
    style: {
      float: 'left',
      font: 'var(--fw-medium) 58px/0.8 var(--font-display)',
      color: 'var(--qz-charcoal)',
      margin: '4px 10px 0 0'
    }
  }, p[0]) : null, i === 0 ? p.slice(1) : p)), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 7,
      flexWrap: 'wrap',
      marginTop: 14
    }
  }, /*#__PURE__*/React.createElement(Tag, {
    href: "#"
  }, "Live Aid"), /*#__PURE__*/React.createElement(Tag, {
    href: "#"
  }, "1985"), /*#__PURE__*/React.createElement(Tag, {
    href: "#"
  }, "Wembley"))));
}
Object.assign(window, {
  MobileHome,
  MobileNews,
  MobileGallery,
  MobileArticle
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/website/MobileScreens.jsx", error: String((e && e.message) || e) }); }

// ui_kits/website/Pages1.jsx
try { (() => {
// Shared page hero band + News Index + Stories Index.
function PageHero({
  eyebrow,
  title,
  lead,
  count
}) {
  return /*#__PURE__*/React.createElement("section", {
    style: {
      background: 'var(--qz-black)',
      position: 'relative',
      overflow: 'hidden'
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: "../../assets/crest-white.png",
    alt: "",
    style: {
      position: 'absolute',
      right: -60,
      top: -50,
      width: 300,
      opacity: 0.06,
      pointerEvents: 'none'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 'var(--container-max)',
      margin: '0 auto',
      padding: '72px var(--gutter-lg) 64px',
      position: 'relative'
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "qz-eyebrow",
    style: {
      color: 'var(--qz-gold)',
      marginBottom: 18
    }
  }, eyebrow), /*#__PURE__*/React.createElement("h1", {
    style: {
      font: 'var(--fw-medium) clamp(40px, 5vw, 64px)/1.02 var(--font-display)',
      letterSpacing: '-0.015em',
      color: 'var(--qz-white)',
      margin: 0
    }
  }, title), lead && /*#__PURE__*/React.createElement("p", {
    style: {
      font: 'var(--fw-regular) 19px/1.55 var(--font-body)',
      color: 'rgba(255,255,255,0.7)',
      margin: '20px 0 0',
      maxWidth: 620
    }
  }, lead), count && /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-medium) 13px/1 var(--font-body)',
      textTransform: 'uppercase',
      letterSpacing: '0.1em',
      color: 'rgba(255,255,255,0.45)',
      marginTop: 26
    }
  }, count)));
}
function NewsIndex({
  onOpen
}) {
  const {
    Input,
    Tag,
    IconButton
  } = window.QueenzoneDesignSystem_6c12e8;
  const news = window.QZ_DATA.news;
  const years = window.QZ_DATA.newsYears;
  const [year, setYear] = React.useState('All');
  return /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement(PageHero, {
    eyebrow: "The Archive",
    title: "News",
    lead: "Four decades of Queen news, restored from the original Queenzone.com archive and presented in full.",
    count: "4,000+ articles"
  }), /*#__PURE__*/React.createElement("section", {
    style: {
      maxWidth: 'var(--container-max)',
      margin: '0 auto',
      padding: '48px var(--gutter-lg) 96px'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: 20,
      flexWrap: 'wrap',
      paddingBottom: 28,
      marginBottom: 8,
      borderBottom: '1px solid var(--border-default)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 8,
      flexWrap: 'wrap'
    }
  }, years.map(y => /*#__PURE__*/React.createElement(Tag, {
    key: y,
    href: "#",
    active: y === year,
    onClick: e => {
      e.preventDefault();
      setYear(y);
    }
  }, y))), /*#__PURE__*/React.createElement("div", {
    style: {
      width: 260
    }
  }, /*#__PURE__*/React.createElement(Input, {
    size: "sm",
    placeholder: "Search the news archive\u2026",
    iconLeft: /*#__PURE__*/React.createElement("i", {
      "data-lucide": "search",
      style: {
        width: 17,
        height: 17
      }
    })
  }))), /*#__PURE__*/React.createElement("div", null, news.map((n, i) => /*#__PURE__*/React.createElement("a", {
    key: i,
    href: "#",
    onClick: e => {
      e.preventDefault();
      onOpen && onOpen({
        image: window.QZ_DATA.hero.image,
        category: n.cat,
        title: n.title,
        excerpt: n.excerpt,
        meta: n.date
      });
    },
    style: {
      display: 'grid',
      gridTemplateColumns: '140px 1fr 24px',
      alignItems: 'center',
      gap: 28,
      padding: '26px 8px',
      textDecoration: 'none',
      borderBottom: '1px solid var(--hairline)'
    },
    onMouseEnter: e => {
      e.currentTarget.style.background = 'var(--qz-grey-50)';
      const t = e.currentTarget.querySelector('.nt');
      if (t) t.style.color = 'var(--qz-blue)';
    },
    onMouseLeave: e => {
      e.currentTarget.style.background = 'transparent';
      const t = e.currentTarget.querySelector('.nt');
      if (t) t.style.color = 'var(--text-primary)';
    }
  }, /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-semibold) 13px/1 var(--font-titling)',
      letterSpacing: '0.1em',
      textTransform: 'uppercase',
      color: 'var(--accent-archive)'
    }
  }, n.date), /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-medium) 11px/1 var(--font-body)',
      letterSpacing: '0.08em',
      textTransform: 'uppercase',
      color: 'var(--text-muted)',
      marginTop: 8
    }
  }, n.cat)), /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement("h3", {
    className: "nt",
    style: {
      font: 'var(--fw-semibold) 23px/1.25 var(--font-display)',
      color: 'var(--text-primary)',
      margin: '0 0 6px',
      transition: 'color var(--dur-fast) var(--ease-out)'
    }
  }, n.title), /*#__PURE__*/React.createElement("p", {
    style: {
      font: 'var(--fw-regular) 15px/1.5 var(--font-body)',
      color: 'var(--text-secondary)',
      margin: 0
    }
  }, n.excerpt)), /*#__PURE__*/React.createElement("i", {
    "data-lucide": "arrow-up-right",
    style: {
      width: 20,
      height: 20,
      color: 'var(--text-muted)'
    }
  })))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      justifyContent: 'center',
      marginTop: 48
    }
  }, /*#__PURE__*/React.createElement("button", {
    style: {
      font: 'var(--fw-medium) 13px/1 var(--font-body)',
      letterSpacing: '0.06em',
      textTransform: 'uppercase',
      color: 'var(--qz-charcoal)',
      background: 'transparent',
      border: '1px solid var(--border-strong)',
      borderRadius: 'var(--radius-sm)',
      padding: '14px 32px',
      cursor: 'pointer'
    }
  }, "Load earlier news"))));
}
function StoriesIndex({
  onOpen
}) {
  const {
    SectionHeader,
    ArticleCard,
    Badge,
    Tag
  } = window.QueenzoneDesignSystem_6c12e8;
  const stories = window.QZ_DATA.featured;
  const all = stories.concat(window.QZ_DATA.restored.map(r => ({
    ...r,
    excerpt: 'A restored feature from the Queenzone.com archive.'
  })));
  const tags = window.QZ_DATA.tags;
  const [active, setActive] = React.useState('All');
  const lead = all[0];
  return /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement(PageHero, {
    eyebrow: "Long-form",
    title: "Stories",
    lead: "In-depth features, essays and oral histories \u2014 the long reads from the Queenzone.com archive.",
    count: "100+ features"
  }), /*#__PURE__*/React.createElement("section", {
    style: {
      maxWidth: 'var(--container-max)',
      margin: '0 auto',
      padding: '56px var(--gutter-lg) 40px'
    }
  }, /*#__PURE__*/React.createElement("a", {
    href: "#",
    onClick: e => {
      e.preventDefault();
      onOpen && onOpen(lead);
    },
    style: {
      display: 'grid',
      gridTemplateColumns: '1.15fr 1fr',
      gap: 48,
      alignItems: 'center',
      textDecoration: 'none',
      paddingBottom: 56,
      marginBottom: 8,
      borderBottom: '1px solid var(--hairline)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'relative',
      overflow: 'hidden',
      borderRadius: 'var(--radius-md)',
      aspectRatio: '3 / 2',
      background: 'var(--qz-grey-200)'
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: lead.image,
    alt: "",
    style: {
      width: '100%',
      height: '100%',
      objectFit: 'cover',
      filter: 'grayscale(1)'
    }
  })), /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement("div", {
    style: {
      marginBottom: 18
    }
  }, /*#__PURE__*/React.createElement(Badge, {
    tone: "editorial",
    variant: "solid"
  }, "Featured")), /*#__PURE__*/React.createElement("h2", {
    style: {
      font: 'var(--fw-medium) 42px/1.05 var(--font-display)',
      letterSpacing: '-0.015em',
      color: 'var(--text-primary)',
      margin: '0 0 16px'
    }
  }, lead.title), /*#__PURE__*/React.createElement("p", {
    style: {
      font: 'var(--fw-regular) 18px/1.6 var(--font-body)',
      color: 'var(--text-secondary)',
      margin: '0 0 18px'
    }
  }, lead.excerpt), /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-medium) 12px/1 var(--font-body)',
      textTransform: 'uppercase',
      letterSpacing: '0.06em',
      color: 'var(--text-muted)'
    }
  }, lead.meta)))), /*#__PURE__*/React.createElement("section", {
    style: {
      maxWidth: 'var(--container-max)',
      margin: '0 auto',
      padding: '0 var(--gutter-lg) 96px'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 9,
      flexWrap: 'wrap',
      marginBottom: 44
    }
  }, tags.map(t => /*#__PURE__*/React.createElement(Tag, {
    key: t,
    href: "#",
    active: t === active,
    onClick: e => {
      e.preventDefault();
      setActive(t);
    }
  }, t))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: 'repeat(3, 1fr)',
      gap: 36
    }
  }, all.slice(1).map(s => /*#__PURE__*/React.createElement(ArticleCard, {
    key: s.title,
    image: s.image,
    category: s.category,
    title: s.title,
    excerpt: s.excerpt,
    meta: s.meta,
    onClick: e => {
      e.preventDefault();
      onOpen && onOpen(s);
    },
    badge: s.badge ? /*#__PURE__*/React.createElement(Badge, {
      tone: s.badge.tone,
      variant: "solid"
    }, s.badge.label) : null
  })))));
}
window.PageHero = PageHero;
window.NewsIndex = NewsIndex;
window.StoriesIndex = StoriesIndex;
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/website/Pages1.jsx", error: String((e && e.message) || e) }); }

// ui_kits/website/Pages2.jsx
try { (() => {
// Photo Gallery (with lightbox) + Timeline page.
function PhotoGallery() {
  const {
    Tag,
    IconButton
  } = window.QueenzoneDesignSystem_6c12e8;
  const photos = window.QZ_DATA.gallery;
  const filters = window.QZ_DATA.galleryFilters;
  const [filter, setFilter] = React.useState('All');
  const [active, setActive] = React.useState(null);
  const shown = filter === 'All' ? photos : photos.filter(p => p.cat === filter);
  React.useEffect(() => {
    window.lucide && window.lucide.createIcons();
  }, [active, filter]);
  const span = s => s === 'tall' ? {
    gridRow: 'span 2'
  } : s === 'wide' ? {
    gridColumn: 'span 2'
  } : {};
  return /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement(PageHero, {
    eyebrow: "The Photographic Archive",
    title: "Photography",
    lead: "Tens of thousands of photographs \u2014 live, studio and backstage \u2014 scanned and restored from the original community archive.",
    count: "Tens of thousands of images"
  }), /*#__PURE__*/React.createElement("section", {
    style: {
      maxWidth: 'var(--container-max)',
      margin: '0 auto',
      padding: '40px var(--gutter-lg) 96px'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 9,
      flexWrap: 'wrap',
      paddingBottom: 32,
      marginBottom: 36,
      borderBottom: '1px solid var(--border-default)'
    }
  }, filters.map(f => /*#__PURE__*/React.createElement(Tag, {
    key: f,
    href: "#",
    active: f === filter,
    onClick: e => {
      e.preventDefault();
      setFilter(f);
    }
  }, f))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: 'repeat(4, 1fr)',
      gridAutoRows: '200px',
      gap: 14
    }
  }, shown.map((p, i) => /*#__PURE__*/React.createElement("figure", {
    key: i,
    onClick: () => setActive(p),
    style: {
      position: 'relative',
      margin: 0,
      overflow: 'hidden',
      borderRadius: 'var(--radius-md)',
      background: 'var(--qz-grey-200)',
      cursor: 'pointer',
      ...span(p.span)
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: p.image,
    alt: p.caption,
    style: {
      width: '100%',
      height: '100%',
      objectFit: 'cover',
      filter: 'grayscale(1)',
      transition: 'transform var(--dur-slow) var(--ease-out), filter var(--dur-slow) var(--ease-out)'
    },
    onMouseEnter: e => {
      e.currentTarget.style.transform = 'scale(1.05)';
      e.currentTarget.style.filter = 'grayscale(0)';
    },
    onMouseLeave: e => {
      e.currentTarget.style.transform = 'scale(1)';
      e.currentTarget.style.filter = 'grayscale(1)';
    }
  }), /*#__PURE__*/React.createElement("figcaption", {
    style: {
      position: 'absolute',
      left: 0,
      right: 0,
      bottom: 0,
      padding: '26px 14px 12px',
      background: 'var(--scrim-soft)',
      display: 'flex',
      justifyContent: 'space-between',
      alignItems: 'flex-end',
      gap: 8,
      font: 'var(--fw-medium) 11.5px/1.3 var(--font-body)',
      textTransform: 'uppercase',
      letterSpacing: '0.05em',
      color: 'rgba(255,255,255,0.92)',
      pointerEvents: 'none'
    }
  }, /*#__PURE__*/React.createElement("span", null, p.caption), /*#__PURE__*/React.createElement("span", {
    style: {
      color: 'var(--qz-gold)'
    }
  }, p.year)))))), active && /*#__PURE__*/React.createElement("div", {
    onClick: () => setActive(null),
    style: {
      position: 'fixed',
      inset: 0,
      zIndex: 120,
      background: 'rgba(10,10,10,0.92)',
      backdropFilter: 'blur(6px)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      padding: 40,
      animation: 'qzFade 240ms ease'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      top: 24,
      right: 28
    }
  }, /*#__PURE__*/React.createElement(IconButton, {
    label: "Close",
    variant: "outline",
    onDark: true,
    onClick: () => setActive(null)
  }, /*#__PURE__*/React.createElement("i", {
    "data-lucide": "x",
    style: {
      width: 20,
      height: 20
    }
  }))), /*#__PURE__*/React.createElement("figure", {
    onClick: e => e.stopPropagation(),
    style: {
      margin: 0,
      maxWidth: 'min(1000px, 90vw)',
      maxHeight: '86vh',
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center'
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: active.image,
    alt: active.caption,
    style: {
      maxWidth: '100%',
      maxHeight: '76vh',
      objectFit: 'contain',
      borderRadius: 'var(--radius-md)',
      filter: 'grayscale(1)'
    }
  }), /*#__PURE__*/React.createElement("figcaption", {
    style: {
      marginTop: 20,
      display: 'flex',
      gap: 16,
      alignItems: 'center',
      font: 'var(--fw-medium) 13px/1 var(--font-body)',
      textTransform: 'uppercase',
      letterSpacing: '0.08em',
      color: 'rgba(255,255,255,0.8)'
    }
  }, /*#__PURE__*/React.createElement("span", null, active.caption), /*#__PURE__*/React.createElement("span", {
    style: {
      color: 'var(--qz-gold)'
    }
  }, active.year), /*#__PURE__*/React.createElement("span", {
    style: {
      color: 'rgba(255,255,255,0.4)'
    }
  }, active.cat)))));
}
function TimelinePage() {
  const items = window.QZ_DATA.timelineFull;
  return /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement(PageHero, {
    eyebrow: "Five Decades",
    title: "Timeline",
    lead: "The story of Queen, year by year \u2014 a guided path through the moments that defined the band and gathered the community."
  }), /*#__PURE__*/React.createElement("section", {
    style: {
      background: 'var(--qz-warm-white)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 880,
      margin: '0 auto',
      padding: '80px var(--gutter-lg) 100px',
      position: 'relative'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      left: 'calc(50% - 0.5px)',
      top: 80,
      bottom: 100,
      width: 1,
      background: 'var(--border-strong)'
    }
  }), items.map((t, i) => {
    const left = i % 2 === 0;
    return /*#__PURE__*/React.createElement("div", {
      key: i,
      style: {
        display: 'grid',
        gridTemplateColumns: '1fr 1fr',
        columnGap: 56,
        marginBottom: 44,
        position: 'relative'
      }
    }, /*#__PURE__*/React.createElement("span", {
      style: {
        position: 'absolute',
        left: 'calc(50% - 5px)',
        top: 8,
        width: 10,
        height: 10,
        borderRadius: '50%',
        background: 'var(--qz-gold)',
        boxShadow: '0 0 0 5px var(--qz-warm-white)'
      }
    }), /*#__PURE__*/React.createElement("div", {
      style: {
        gridColumn: left ? 1 : 2,
        textAlign: left ? 'right' : 'left',
        paddingRight: left ? 8 : 0,
        paddingLeft: left ? 0 : 8
      }
    }, /*#__PURE__*/React.createElement("div", {
      style: {
        font: 'var(--fw-semibold) 34px/1 var(--font-titling)',
        letterSpacing: '0.04em',
        color: 'var(--qz-gold-deep)',
        marginBottom: 12
      }
    }, t.year), /*#__PURE__*/React.createElement("h3", {
      style: {
        font: 'var(--fw-semibold) 24px/1.2 var(--font-display)',
        color: 'var(--text-primary)',
        margin: '0 0 8px'
      }
    }, t.title), /*#__PURE__*/React.createElement("p", {
      style: {
        font: 'var(--fw-regular) 16px/1.6 var(--font-body)',
        color: 'var(--text-secondary)',
        margin: 0
      }
    }, t.text)));
  }))));
}
window.PhotoGallery = PhotoGallery;
window.TimelinePage = TimelinePage;
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/website/Pages2.jsx", error: String((e && e.message) || e) }); }

// ui_kits/website/Sections1.jsx
try { (() => {
// Homepage sections — part 1: Explore the Archive, Featured Stories, Photography.
const QZWrap = ({
  children,
  bg,
  style
}) => /*#__PURE__*/React.createElement("section", {
  style: {
    background: bg || 'transparent',
    ...style
  }
}, /*#__PURE__*/React.createElement("div", {
  style: {
    maxWidth: 'var(--container-max)',
    margin: '0 auto',
    padding: '88px var(--gutter-lg)'
  }
}, children));
function ExploreArchive({
  onNav
}) {
  const {
    Badge
  } = window.QueenzoneDesignSystem_6c12e8;
  const items = window.QZ_DATA.explore;
  const pageFor = {
    'News Archive': 'news',
    'Long-form Stories': 'stories',
    'Photography': 'gallery',
    'Forum History': 'home'
  };
  return /*#__PURE__*/React.createElement(QZWrap, {
    bg: "var(--qz-warm-white)"
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      textAlign: 'center',
      marginBottom: 48
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "qz-eyebrow",
    style: {
      color: 'var(--accent-archive)',
      marginBottom: 14
    }
  }, "The Queenzone.com Archive"), /*#__PURE__*/React.createElement("h2", {
    style: {
      font: 'var(--type-h2)',
      margin: 0
    }
  }, "Explore the Archive"), /*#__PURE__*/React.createElement("p", {
    style: {
      font: 'var(--type-lead)',
      color: 'var(--text-secondary)',
      maxWidth: 580,
      margin: '16px auto 0'
    }
  }, "Decades of news, stories, photography and conversation from the original community \u2014 preserved, organised and published.")), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: 'repeat(4, 1fr)',
      gap: 20
    }
  }, items.map(it => /*#__PURE__*/React.createElement("a", {
    key: it.label,
    href: "#",
    onClick: e => {
      e.preventDefault();
      onNav && onNav(pageFor[it.label] || 'home');
    },
    style: {
      display: 'flex',
      flexDirection: 'column',
      gap: 16,
      padding: '30px 26px',
      background: 'var(--qz-white)',
      border: '1px solid var(--border-default)',
      borderRadius: 'var(--radius-sm)',
      textDecoration: 'none',
      transition: 'box-shadow var(--dur-base) var(--ease-out), transform var(--dur-base) var(--ease-out)'
    },
    onMouseEnter: e => {
      e.currentTarget.style.boxShadow = 'var(--shadow-lift)';
      e.currentTarget.style.transform = 'translateY(-3px)';
    },
    onMouseLeave: e => {
      e.currentTarget.style.boxShadow = 'none';
      e.currentTarget.style.transform = 'translateY(0)';
    }
  }, /*#__PURE__*/React.createElement("i", {
    "data-lucide": it.icon,
    style: {
      width: 28,
      height: 28,
      color: 'var(--qz-charcoal)',
      strokeWidth: 1.4
    }
  }), /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-semibold) 19px/1.3 var(--font-display)',
      color: 'var(--text-primary)',
      marginBottom: 6
    }
  }, it.label), /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-medium) 13px/1 var(--font-body)',
      textTransform: 'uppercase',
      letterSpacing: '0.06em',
      color: 'var(--text-muted)'
    }
  }, it.count))))));
}
function FeaturedStories({
  onOpen
}) {
  const {
    SectionHeader,
    ArticleCard,
    Badge,
    Button,
    Tag
  } = window.QueenzoneDesignSystem_6c12e8;
  const stories = window.QZ_DATA.featured;
  const tags = window.QZ_DATA.tags;
  const [active, setActive] = React.useState('All');
  return /*#__PURE__*/React.createElement(QZWrap, {
    bg: "var(--qz-black)"
  }, /*#__PURE__*/React.createElement(SectionHeader, {
    onDark: true,
    eyebrow: "Editorial",
    title: "Featured Stories",
    action: /*#__PURE__*/React.createElement(Button, {
      variant: "ghost",
      size: "sm",
      style: {
        color: 'var(--qz-white)'
      },
      iconRight: /*#__PURE__*/React.createElement("i", {
        "data-lucide": "arrow-right",
        style: {
          width: 15,
          height: 15
        }
      })
    }, "View all")
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 9,
      flexWrap: 'wrap',
      margin: '24px 0 40px'
    }
  }, tags.map(t => /*#__PURE__*/React.createElement(Tag, {
    key: t,
    href: "#",
    onDark: true,
    active: t === active,
    onClick: e => {
      e.preventDefault();
      setActive(t);
    }
  }, t))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: 'repeat(3, 1fr)',
      gap: 36
    }
  }, stories.map(s => /*#__PURE__*/React.createElement(ArticleCard, {
    key: s.title,
    onDark: true,
    image: s.image,
    category: s.category,
    title: s.title,
    excerpt: s.excerpt,
    meta: s.meta,
    onClick: e => {
      e.preventDefault();
      onOpen && onOpen(s);
    },
    badge: s.badge ? /*#__PURE__*/React.createElement(Badge, {
      tone: s.badge.tone,
      variant: "solid"
    }, s.badge.label) : null
  }))));
}
function Photography() {
  const {
    SectionHeader,
    Button
  } = window.QueenzoneDesignSystem_6c12e8;
  const photos = window.QZ_DATA.photography;
  return /*#__PURE__*/React.createElement("section", {
    style: {
      background: 'var(--qz-warm-white)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 'var(--container-max)',
      margin: '0 auto',
      padding: '88px var(--gutter-lg)'
    }
  }, /*#__PURE__*/React.createElement(SectionHeader, {
    eyebrow: "The Photographic Archive",
    title: "Featured Photography",
    action: /*#__PURE__*/React.createElement(Button, {
      variant: "secondary",
      size: "sm"
    }, "Browse gallery")
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: '2fr 1fr 1fr',
      gridTemplateRows: '230px 230px',
      gap: 14,
      marginTop: 40
    }
  }, photos.map((p, i) => /*#__PURE__*/React.createElement("figure", {
    key: i,
    style: {
      position: 'relative',
      margin: 0,
      overflow: 'hidden',
      borderRadius: 'var(--radius-md)',
      background: 'var(--qz-grey-200)',
      gridColumn: i === 0 ? 'span 1' : undefined,
      gridRow: i === 0 ? 'span 2' : undefined
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: p.image,
    alt: p.caption,
    style: {
      width: '100%',
      height: '100%',
      objectFit: 'cover',
      filter: 'grayscale(1)',
      transition: 'transform var(--dur-slow) var(--ease-out)'
    },
    onMouseEnter: e => e.currentTarget.style.transform = 'scale(1.04)',
    onMouseLeave: e => e.currentTarget.style.transform = 'scale(1)'
  }), /*#__PURE__*/React.createElement("figcaption", {
    style: {
      position: 'absolute',
      left: 0,
      right: 0,
      bottom: 0,
      padding: '28px 16px 14px',
      background: 'var(--scrim-soft)',
      font: 'var(--fw-medium) 12px/1.3 var(--font-body)',
      textTransform: 'uppercase',
      letterSpacing: '0.06em',
      color: 'rgba(255,255,255,0.92)'
    }
  }, p.caption))))));
}
window.ExploreArchive = ExploreArchive;
window.FeaturedStories = FeaturedStories;
window.Photography = Photography;
window.QZWrap = QZWrap;
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/website/Sections1.jsx", error: String((e && e.message) || e) }); }

// ui_kits/website/Sections2.jsx
try { (() => {
// Homepage sections — part 2: This Day, Popular Discussions, Recently Restored, Timeline.
function ThisDay() {
  const {
    SectionHeader
  } = window.QueenzoneDesignSystem_6c12e8;
  const items = window.QZ_DATA.thisDay;
  return /*#__PURE__*/React.createElement("section", {
    style: {
      background: 'var(--qz-black)',
      position: 'relative',
      overflow: 'hidden'
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: "../../assets/crest-white.png",
    alt: "",
    style: {
      position: 'absolute',
      right: -70,
      top: -40,
      width: 300,
      opacity: 0.05,
      pointerEvents: 'none'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 'var(--container-max)',
      margin: '0 auto',
      padding: '88px var(--gutter-lg)',
      position: 'relative'
    }
  }, /*#__PURE__*/React.createElement(SectionHeader, {
    onDark: true,
    eyebrow: "On This Day",
    title: "This Day in Queen History"
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: 'repeat(3, 1fr)',
      gap: 0,
      marginTop: 40,
      borderTop: '1px solid var(--border-on-dark)'
    }
  }, items.map((it, i) => /*#__PURE__*/React.createElement("div", {
    key: i,
    style: {
      padding: '32px 28px 32px 0',
      borderRight: i < 2 ? '1px solid var(--border-on-dark)' : 'none',
      paddingLeft: i > 0 ? 28 : 0
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-semibold) 14px/1 var(--font-titling)',
      letterSpacing: '0.14em',
      textTransform: 'uppercase',
      color: 'var(--qz-gold)',
      marginBottom: 16
    }
  }, it.date), /*#__PURE__*/React.createElement("p", {
    style: {
      font: 'var(--fw-regular) 18px/1.5 var(--font-display)',
      color: 'rgba(255,255,255,0.86)',
      margin: 0
    }
  }, it.text))))));
}
function Discussions() {
  const {
    SectionHeader,
    Button,
    Tag
  } = window.QueenzoneDesignSystem_6c12e8;
  const items = window.QZ_DATA.discussions;
  const QZWrap = window.QZWrap;
  return /*#__PURE__*/React.createElement(QZWrap, null, /*#__PURE__*/React.createElement(SectionHeader, {
    eyebrow: "The Community",
    title: "Popular Discussions",
    action: /*#__PURE__*/React.createElement(Button, {
      variant: "ghost",
      size: "sm"
    }, "Visit the forum")
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      marginTop: 36,
      border: '1px solid var(--border-default)',
      borderRadius: 'var(--radius-sm)',
      overflow: 'hidden'
    }
  }, items.map((d, i) => /*#__PURE__*/React.createElement("a", {
    key: i,
    href: "#",
    onClick: e => e.preventDefault(),
    style: {
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: 24,
      padding: '22px 28px',
      textDecoration: 'none',
      borderTop: i > 0 ? '1px solid var(--border-default)' : 'none',
      background: 'var(--qz-white)',
      transition: 'background var(--dur-fast) var(--ease-out)'
    },
    onMouseEnter: e => e.currentTarget.style.background = 'var(--qz-grey-50)',
    onMouseLeave: e => e.currentTarget.style.background = 'var(--qz-white)'
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 20,
      minWidth: 0
    }
  }, /*#__PURE__*/React.createElement("i", {
    "data-lucide": "message-circle",
    style: {
      width: 20,
      height: 20,
      color: 'var(--text-muted)',
      flexShrink: 0,
      strokeWidth: 1.5
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      minWidth: 0
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-semibold) 17px/1.3 var(--font-body)',
      color: 'var(--text-primary)',
      marginBottom: 5,
      overflow: 'hidden',
      textOverflow: 'ellipsis',
      whiteSpace: 'nowrap'
    }
  }, d.title), /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-medium) 12px/1 var(--font-body)',
      textTransform: 'uppercase',
      letterSpacing: '0.05em',
      color: 'var(--text-muted)'
    }
  }, d.era, " \xB7 ", d.replies.toLocaleString(), " replies \xB7 ", d.last))), /*#__PURE__*/React.createElement("i", {
    "data-lucide": "chevron-right",
    style: {
      width: 18,
      height: 18,
      color: 'var(--text-muted)',
      flexShrink: 0
    }
  })))));
}
function Restored() {
  const {
    SectionHeader,
    ArticleCard,
    Badge,
    Button
  } = window.QueenzoneDesignSystem_6c12e8;
  const items = window.QZ_DATA.restored;
  const QZWrap = window.QZWrap;
  return /*#__PURE__*/React.createElement(QZWrap, {
    bg: "var(--qz-warm-white)"
  }, /*#__PURE__*/React.createElement(SectionHeader, {
    eyebrow: "From the Vaults",
    title: "Recently Restored",
    action: /*#__PURE__*/React.createElement(Button, {
      variant: "ghost",
      size: "sm"
    }, "All restorations")
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: '1fr 1fr',
      gap: 40,
      marginTop: 40
    }
  }, items.map(r => /*#__PURE__*/React.createElement(ArticleCard, {
    key: r.title,
    layout: "horizontal",
    image: r.image,
    category: r.category,
    title: r.title,
    meta: r.meta,
    badge: /*#__PURE__*/React.createElement(Badge, {
      tone: "special",
      variant: "solid"
    }, "Restored")
  }))));
}
function Timeline() {
  const items = window.QZ_DATA.timeline;
  return /*#__PURE__*/React.createElement("section", {
    style: {
      background: 'var(--qz-black)',
      position: 'relative',
      overflow: 'hidden'
    }
  }, /*#__PURE__*/React.createElement("img", {
    src: "../../assets/crest-white.png",
    alt: "",
    style: {
      position: 'absolute',
      left: -80,
      bottom: -60,
      width: 380,
      opacity: 0.05,
      pointerEvents: 'none'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      maxWidth: 'var(--container-max)',
      margin: '0 auto',
      padding: '92px var(--gutter-lg)',
      position: 'relative'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      textAlign: 'center',
      marginBottom: 56
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "qz-eyebrow",
    style: {
      color: 'var(--qz-gold)',
      marginBottom: 14
    }
  }, "Five Decades"), /*#__PURE__*/React.createElement("h2", {
    style: {
      font: 'var(--type-h2)',
      color: 'var(--qz-white)',
      margin: 0
    }
  }, "Timeline Highlights")), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: 'repeat(4, 1fr)',
      gap: 28
    }
  }, items.map((t, i) => /*#__PURE__*/React.createElement("div", {
    key: i,
    style: {
      paddingTop: 26,
      borderTop: '1px solid var(--border-on-dark)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      font: 'var(--fw-semibold) 30px/1 var(--font-titling)',
      letterSpacing: '0.04em',
      color: 'var(--qz-gold)',
      marginBottom: 16
    }
  }, t.year), /*#__PURE__*/React.createElement("p", {
    style: {
      font: 'var(--fw-regular) 16px/1.55 var(--font-body)',
      color: 'rgba(255,255,255,0.78)',
      margin: 0
    }
  }, t.text))))));
}
window.ThisDay = ThisDay;
window.Discussions = Discussions;
window.Restored = Restored;
window.Timeline = Timeline;
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/website/Sections2.jsx", error: String((e && e.message) || e) }); }

// ui_kits/website/data.js
try { (() => {
// Queenzone homepage content — fictional but plausible archive material.
// Tone: knowledgeable, passionate, respectful. No clickbait.
window.QZ_DATA = {
  hero: {
    category: 'The Archive',
    title: 'Twenty-five years of the Queen internet zone',
    standfirst: 'From hand-coded HTML in 1999 to the community\u2019s final form — every era of Queenzone.com, restored and morphed into one living home.',
    meta: 'Est. 1999 · queenzone.com',
    image: '../../assets/img-hero.jpg'
  },
  explore: [{
    label: 'News Archive',
    count: '4,000+ articles',
    icon: 'newspaper',
    tone: 'cta'
  }, {
    label: 'Long-form Stories',
    count: '100+ features',
    icon: 'book-open',
    tone: 'editorial'
  }, {
    label: 'Photography',
    count: 'Tens of thousands',
    icon: 'camera',
    tone: 'archive'
  }, {
    label: 'Forum History',
    count: '100,000+ posts',
    icon: 'messages-square',
    tone: 'cta'
  }],
  featured: [{
    image: '../../assets/img-studio.jpg',
    category: 'Recording',
    title: 'Inside the Making of Bohemian Rhapsody',
    excerpt: 'Six weeks, three studios and a chorus recorded more than 180 times.',
    meta: 'Restored archive · 12 min read',
    badge: {
      tone: 'archive',
      label: 'Archive'
    }
  }, {
    image: '../../assets/img-portrait.jpg',
    category: 'In Memoriam',
    title: 'Freddie: The Voice That Defined an Era',
    excerpt: 'A four-octave range, and a presence no stadium could contain.',
    meta: '5 September · 9 min read',
    badge: {
      tone: 'editorial',
      label: 'Featured'
    }
  }, {
    image: '../../assets/img-crowd.jpg',
    category: 'Live History',
    title: 'The Magic Tour, Night by Night',
    excerpt: 'The 1986 run that would become the final tour with all four members.',
    meta: 'Restored archive · 15 min read',
    badge: null
  }],
  photography: [{
    image: '../../assets/img-stage.jpg',
    caption: 'Wembley Stadium, July 1986'
  }, {
    image: '../../assets/img-portrait.jpg',
    caption: 'Studio portrait, 1974'
  }, {
    image: '../../assets/img-crowd.jpg',
    caption: 'Hyde Park, September 1976'
  }, {
    image: '../../assets/img-studio.jpg',
    caption: 'Mountain Studios, Montreux'
  }],
  thisDay: [{
    date: '13 Jul 1985',
    text: 'Queen perform at Live Aid, Wembley Stadium, in a set later voted the greatest live performance in rock history.'
  }, {
    date: '31 Oct 1975',
    text: '\u2018Bohemian Rhapsody\u2019 is released as a single, breaking every rule of contemporary radio.'
  }, {
    date: '20 Apr 1992',
    text: 'The Freddie Mercury Tribute Concert is held at Wembley before 72,000 people.'
  }],
  discussions: [{
    title: 'The definitive ranking of every studio album',
    replies: 1284,
    era: 'Albums',
    last: '2h ago'
  }, {
    title: 'Unheard Montreux session tapes — what do we know?',
    replies: 642,
    era: 'Recordings',
    last: '5h ago'
  }, {
    title: 'Restoring the 1977 News of the World tour photos',
    replies: 318,
    era: 'Photography',
    last: '1d ago'
  }, {
    title: 'Brian May\u2019s Red Special: every documented modification',
    replies: 906,
    era: 'Gear',
    last: '2d ago'
  }],
  restored: [{
    image: '../../assets/img-crowd.jpg',
    category: 'Restored',
    title: 'Earls Court 1977: The Lost Negatives',
    meta: 'Restored June 2026'
  }, {
    image: '../../assets/img-studio.jpg',
    category: 'Restored',
    title: 'The Trident Studios Demo Reels',
    meta: 'Restored May 2026'
  }],
  timeline: [{
    year: '1971',
    text: 'The classic line-up is complete as John Deacon joins.'
  }, {
    year: '1975',
    text: 'A Night at the Opera becomes the most expensive album ever made.'
  }, {
    year: '1985',
    text: 'Live Aid cements Queen as the greatest live band of their generation.'
  }, {
    year: '1991',
    text: 'The world loses Freddie Mercury; the legacy endures.'
  }],
  tags: ['All', 'A Night at the Opera', 'Live Aid', 'Freddie Mercury', 'Brian May', '1970s', '1980s', 'Recordings'],
  // ---- News index (chronological archive list) ----
  newsYears: ['All', '1985', '1986', '1991', '1992', '2011'],
  news: [{
    date: '13 Jul 1985',
    cat: 'Live',
    title: 'Queen confirmed for Live Aid at Wembley Stadium',
    excerpt: 'The band will take a 20-minute slot on the afternoon bill alongside the era\u2019s biggest names.'
  }, {
    date: '02 Jun 1986',
    cat: 'Tour',
    title: 'The Magic Tour opens in Stockholm',
    excerpt: 'A new stage design and the largest production the band has yet mounted across Europe.'
  }, {
    date: '12 Aug 1986',
    cat: 'Live',
    title: 'Knebworth Park draws a record crowd',
    excerpt: 'What would become the final concert with all four original members.'
  }, {
    date: '24 Nov 1991',
    cat: 'News',
    title: 'A statement from the band',
    excerpt: 'The community gathers as news reaches fans across the world.'
  }, {
    date: '20 Apr 1992',
    cat: 'Tribute',
    title: 'The Tribute Concert fills Wembley once more',
    excerpt: '72,000 attend as artists from across music pay their respects.'
  }, {
    date: '07 Mar 2011',
    cat: 'Reissue',
    title: 'The remastered studio catalogue is announced',
    excerpt: 'Forty years of recordings restored and reissued for a new generation.'
  }],
  // ---- Photo gallery (archive grid) ----
  galleryFilters: ['All', 'Live', 'Studio', 'Portrait', 'Backstage'],
  gallery: [{
    image: '../../assets/img-stage.jpg',
    caption: 'Wembley Stadium',
    year: '1986',
    cat: 'Live',
    span: 'tall'
  }, {
    image: '../../assets/img-portrait.jpg',
    caption: 'Studio portrait',
    year: '1974',
    cat: 'Portrait',
    span: 'tall'
  }, {
    image: '../../assets/img-crowd.jpg',
    caption: 'Hyde Park',
    year: '1976',
    cat: 'Live',
    span: 'wide'
  }, {
    image: '../../assets/img-studio.jpg',
    caption: 'Mountain Studios',
    year: '1978',
    cat: 'Studio',
    span: 'normal'
  }, {
    image: '../../assets/img-hero.jpg',
    caption: 'Live Aid',
    year: '1985',
    cat: 'Live',
    span: 'wide'
  }, {
    image: '../../assets/img-stage.jpg',
    caption: 'Earls Court',
    year: '1977',
    cat: 'Live',
    span: 'normal'
  }, {
    image: '../../assets/img-studio.jpg',
    caption: 'Trident Studios',
    year: '1973',
    cat: 'Studio',
    span: 'normal'
  }, {
    image: '../../assets/img-portrait.jpg',
    caption: 'Backstage, Montreal',
    year: '1981',
    cat: 'Backstage',
    span: 'tall'
  }, {
    image: '../../assets/img-crowd.jpg',
    caption: 'Rock in Rio',
    year: '1985',
    cat: 'Live',
    span: 'wide'
  }],
  // ---- Full timeline ----
  timelineFull: [{
    year: '1970',
    title: 'A band is named',
    text: 'Brian May and Roger Taylor are joined by a new singer, who renames the band Queen.'
  }, {
    year: '1971',
    title: 'The line-up completes',
    text: 'John Deacon joins on bass, completing the classic four-piece.'
  }, {
    year: '1973',
    title: 'The debut album',
    text: 'Queen release their self-titled debut, recorded largely in down-time at Trident Studios.'
  }, {
    year: '1975',
    title: 'A Night at the Opera',
    text: 'The most expensive album ever made to that point, and the arrival of \u2018Bohemian Rhapsody\u2019.'
  }, {
    year: '1981',
    title: 'Greatest Hits',
    text: 'The compilation becomes one of the best-selling albums in history.'
  }, {
    year: '1985',
    title: 'Live Aid',
    text: 'Twenty-one minutes that cement Queen as the greatest live band of their generation.'
  }, {
    year: '1986',
    title: 'The Magic Tour',
    text: 'The final tour with all four original members draws record crowds across Europe.'
  }, {
    year: '1991',
    title: 'A legacy endures',
    text: 'The world loses Freddie Mercury; the music and community carry on.'
  }],
  // ---- Forum / community ----
  forumStats: {
    members: '18,400',
    threads: '12,600',
    posts: '100,000+'
  },
  forumBoards: [{
    name: 'The Music',
    desc: 'Albums, songs, lyrics and the catalogue, track by track.',
    icon: 'disc-3',
    threads: 3120,
    posts: '41,200',
    last: {
      thread: 'Ranking every studio album',
      when: '2h ago'
    }
  }, {
    name: 'Live & Tours',
    desc: 'Setlists, bootlegs and memories from every era of touring.',
    icon: 'mic-2',
    threads: 2480,
    posts: '28,900',
    last: {
      thread: 'Magic Tour — night by night',
      when: '4h ago'
    }
  }, {
    name: 'Recordings & Rarities',
    desc: 'Sessions, outtakes, demos and the hunt for lost tapes.',
    icon: 'radio',
    threads: 1760,
    posts: '19,300',
    last: {
      thread: 'Unheard Montreux session tapes',
      when: '5h ago'
    }
  }, {
    name: 'The Archive Project',
    desc: 'Restoring and cataloguing the original Queenzone.com archive.',
    icon: 'archive',
    threads: 540,
    posts: '6,100',
    last: {
      thread: 'Earls Court 1977 negatives',
      when: '1d ago'
    }
  }, {
    name: 'Gear & Technique',
    desc: 'The Red Special, amps, harmonies and how the sound was made.',
    icon: 'guitar',
    threads: 980,
    posts: '11,400',
    last: {
      thread: 'Brian May\u2019s Red Special mods',
      when: '2d ago'
    }
  }, {
    name: 'The Lounge',
    desc: 'Introductions, off-topic and everything in between.',
    icon: 'armchair',
    threads: 1720,
    posts: '14,800',
    last: {
      thread: 'How did you find Queenzone?',
      when: '6h ago'
    }
  }],
  forumThreads: [{
    title: 'The definitive ranking of every studio album',
    board: 'The Music',
    author: 'brightonrock',
    replies: 1284,
    views: '24.1k',
    when: '2h ago',
    pinned: true
  }, {
    title: 'Unheard Montreux session tapes — what do we know?',
    board: 'Recordings & Rarities',
    author: 'mountain_studio',
    replies: 642,
    views: '11.8k',
    when: '5h ago'
  }, {
    title: 'Restoring the 1977 News of the World tour photos',
    board: 'The Archive Project',
    author: 'negative_space',
    replies: 318,
    views: '6.3k',
    when: '1d ago'
  }, {
    title: 'Brian May\u2019s Red Special: every documented modification',
    board: 'Gear & Technique',
    author: 'sixpence',
    replies: 906,
    views: '18.0k',
    when: '2d ago'
  }, {
    title: 'Your first Queen concert — share the memory',
    board: 'Live & Tours',
    author: 'somebodytolove',
    replies: 2104,
    views: '39.5k',
    when: '3h ago'
  }]
};
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/website/data.js", error: String((e && e.message) || e) }); }

// ui_kits/website/ios-frame.jsx
try { (() => {
// @ds-adherence-ignore -- omelette starter scaffold (raw elements/hex/px by design)

/* BEGIN USAGE */
// iOS.jsx — Simplified iOS 26 (Liquid Glass) device frame
// Based on the iOS 26 UI Kit + Figma status bar spec. No assets, no deps.
// Exports (to window): IOSDevice, IOSStatusBar, IOSNavBar, IOSGlassPill, IOSList, IOSListRow, IOSKeyboard
//
// Usage — wrap your screen content in <IOSDevice> to get the bezel, status bar
// and home indicator (props: title, dark, keyboard):
//
//   <IOSDevice title="Settings">
//     ...your screen content...
//   </IOSDevice>
//   <IOSDevice dark title="Search" keyboard>…</IOSDevice>
/* END USAGE */

// ─────────────────────────────────────────────────────────────
// Status bar
// ─────────────────────────────────────────────────────────────
function IOSStatusBar({
  dark = false,
  time = '9:41'
}) {
  const c = dark ? '#fff' : '#000';
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 154,
      alignItems: 'center',
      justifyContent: 'center',
      padding: '21px 24px 19px',
      boxSizing: 'border-box',
      position: 'relative',
      zIndex: 20,
      width: '100%'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      height: 22,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      paddingTop: 1.5
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: '-apple-system, "SF Pro", system-ui',
      fontWeight: 590,
      fontSize: 17,
      lineHeight: '22px',
      color: c
    }
  }, time)), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      height: 22,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      gap: 7,
      paddingTop: 1,
      paddingRight: 1
    }
  }, /*#__PURE__*/React.createElement("svg", {
    width: "19",
    height: "12",
    viewBox: "0 0 19 12"
  }, /*#__PURE__*/React.createElement("rect", {
    x: "0",
    y: "7.5",
    width: "3.2",
    height: "4.5",
    rx: "0.7",
    fill: c
  }), /*#__PURE__*/React.createElement("rect", {
    x: "4.8",
    y: "5",
    width: "3.2",
    height: "7",
    rx: "0.7",
    fill: c
  }), /*#__PURE__*/React.createElement("rect", {
    x: "9.6",
    y: "2.5",
    width: "3.2",
    height: "9.5",
    rx: "0.7",
    fill: c
  }), /*#__PURE__*/React.createElement("rect", {
    x: "14.4",
    y: "0",
    width: "3.2",
    height: "12",
    rx: "0.7",
    fill: c
  })), /*#__PURE__*/React.createElement("svg", {
    width: "17",
    height: "12",
    viewBox: "0 0 17 12"
  }, /*#__PURE__*/React.createElement("path", {
    d: "M8.5 3.2C10.8 3.2 12.9 4.1 14.4 5.6L15.5 4.5C13.7 2.7 11.2 1.5 8.5 1.5C5.8 1.5 3.3 2.7 1.5 4.5L2.6 5.6C4.1 4.1 6.2 3.2 8.5 3.2Z",
    fill: c
  }), /*#__PURE__*/React.createElement("path", {
    d: "M8.5 6.8C9.9 6.8 11.1 7.3 12 8.2L13.1 7.1C11.8 5.9 10.2 5.1 8.5 5.1C6.8 5.1 5.2 5.9 3.9 7.1L5 8.2C5.9 7.3 7.1 6.8 8.5 6.8Z",
    fill: c
  }), /*#__PURE__*/React.createElement("circle", {
    cx: "8.5",
    cy: "10.5",
    r: "1.5",
    fill: c
  })), /*#__PURE__*/React.createElement("svg", {
    width: "27",
    height: "13",
    viewBox: "0 0 27 13"
  }, /*#__PURE__*/React.createElement("rect", {
    x: "0.5",
    y: "0.5",
    width: "23",
    height: "12",
    rx: "3.5",
    stroke: c,
    strokeOpacity: "0.35",
    fill: "none"
  }), /*#__PURE__*/React.createElement("rect", {
    x: "2",
    y: "2",
    width: "20",
    height: "9",
    rx: "2",
    fill: c
  }), /*#__PURE__*/React.createElement("path", {
    d: "M25 4.5V8.5C25.8 8.2 26.5 7.2 26.5 6.5C26.5 5.8 25.8 4.8 25 4.5Z",
    fill: c,
    fillOpacity: "0.4"
  }))));
}

// ─────────────────────────────────────────────────────────────
// Liquid glass pill — blur + tint + shine
// ─────────────────────────────────────────────────────────────
function IOSGlassPill({
  children,
  dark = false,
  style = {}
}) {
  return /*#__PURE__*/React.createElement("div", {
    style: {
      height: 44,
      minWidth: 44,
      borderRadius: 9999,
      position: 'relative',
      overflow: 'hidden',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      boxShadow: dark ? '0 2px 6px rgba(0,0,0,0.35), 0 6px 16px rgba(0,0,0,0.2)' : '0 1px 3px rgba(0,0,0,0.07), 0 3px 10px rgba(0,0,0,0.06)',
      ...style
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      inset: 0,
      borderRadius: 9999,
      backdropFilter: 'blur(12px) saturate(180%)',
      WebkitBackdropFilter: 'blur(12px) saturate(180%)',
      background: dark ? 'rgba(120,120,128,0.28)' : 'rgba(255,255,255,0.5)'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      inset: 0,
      borderRadius: 9999,
      boxShadow: dark ? 'inset 1.5px 1.5px 1px rgba(255,255,255,0.15), inset -1px -1px 1px rgba(255,255,255,0.08)' : 'inset 1.5px 1.5px 1px rgba(255,255,255,0.7), inset -1px -1px 1px rgba(255,255,255,0.4)',
      border: dark ? '0.5px solid rgba(255,255,255,0.15)' : '0.5px solid rgba(0,0,0,0.06)'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'relative',
      zIndex: 1,
      display: 'flex',
      alignItems: 'center',
      padding: '0 4px'
    }
  }, children));
}

// ─────────────────────────────────────────────────────────────
// Navigation bar — glass pills + large title
// ─────────────────────────────────────────────────────────────
function IOSNavBar({
  title = 'Title',
  dark = false,
  trailingIcon = true
}) {
  const muted = dark ? 'rgba(255,255,255,0.6)' : '#404040';
  const text = dark ? '#fff' : '#000';
  const pillIcon = content => /*#__PURE__*/React.createElement(IOSGlassPill, {
    dark: dark
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      width: 36,
      height: 36,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center'
    }
  }, content));
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexDirection: 'column',
      gap: 10,
      paddingTop: 62,
      paddingBottom: 10,
      position: 'relative',
      zIndex: 5
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      padding: '0 16px'
    }
  }, pillIcon(/*#__PURE__*/React.createElement("svg", {
    width: "12",
    height: "20",
    viewBox: "0 0 12 20",
    fill: "none",
    style: {
      marginLeft: -1
    }
  }, /*#__PURE__*/React.createElement("path", {
    d: "M10 2L2 10l8 8",
    stroke: muted,
    strokeWidth: "2.5",
    strokeLinecap: "round",
    strokeLinejoin: "round"
  }))), trailingIcon && pillIcon(/*#__PURE__*/React.createElement("svg", {
    width: "22",
    height: "6",
    viewBox: "0 0 22 6"
  }, /*#__PURE__*/React.createElement("circle", {
    cx: "3",
    cy: "3",
    r: "2.5",
    fill: muted
  }), /*#__PURE__*/React.createElement("circle", {
    cx: "11",
    cy: "3",
    r: "2.5",
    fill: muted
  }), /*#__PURE__*/React.createElement("circle", {
    cx: "19",
    cy: "3",
    r: "2.5",
    fill: muted
  })))), /*#__PURE__*/React.createElement("div", {
    style: {
      padding: '0 16px',
      fontFamily: '-apple-system, system-ui',
      fontSize: 34,
      fontWeight: 700,
      lineHeight: '41px',
      color: text,
      letterSpacing: 0.4
    }
  }, title));
}

// ─────────────────────────────────────────────────────────────
// Grouped list (inset card, r:26) + row (52px)
// ─────────────────────────────────────────────────────────────
function IOSListRow({
  title,
  detail,
  icon,
  chevron = true,
  isLast = false,
  dark = false
}) {
  const text = dark ? '#fff' : '#000';
  const sec = dark ? 'rgba(235,235,245,0.6)' : 'rgba(60,60,67,0.6)';
  const ter = dark ? 'rgba(235,235,245,0.3)' : 'rgba(60,60,67,0.3)';
  const sep = dark ? 'rgba(84,84,88,0.65)' : 'rgba(60,60,67,0.12)';
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      minHeight: 52,
      padding: '0 16px',
      position: 'relative',
      fontFamily: '-apple-system, system-ui',
      fontSize: 17,
      letterSpacing: -0.43
    }
  }, icon && /*#__PURE__*/React.createElement("div", {
    style: {
      width: 30,
      height: 30,
      borderRadius: 7,
      background: icon,
      marginRight: 12,
      flexShrink: 0
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      color: text
    }
  }, title), detail && /*#__PURE__*/React.createElement("span", {
    style: {
      color: sec,
      marginRight: 6
    }
  }, detail), chevron && /*#__PURE__*/React.createElement("svg", {
    width: "8",
    height: "14",
    viewBox: "0 0 8 14",
    style: {
      flexShrink: 0
    }
  }, /*#__PURE__*/React.createElement("path", {
    d: "M1 1l6 6-6 6",
    stroke: ter,
    strokeWidth: "2",
    fill: "none",
    strokeLinecap: "round",
    strokeLinejoin: "round"
  })), !isLast && /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      bottom: 0,
      right: 0,
      left: icon ? 58 : 16,
      height: 0.5,
      background: sep
    }
  }));
}
function IOSList({
  header,
  children,
  dark = false
}) {
  const hc = dark ? 'rgba(235,235,245,0.6)' : 'rgba(60,60,67,0.6)';
  const bg = dark ? '#1C1C1E' : '#fff';
  return /*#__PURE__*/React.createElement("div", null, header && /*#__PURE__*/React.createElement("div", {
    style: {
      fontFamily: '-apple-system, system-ui',
      fontSize: 13,
      color: hc,
      textTransform: 'uppercase',
      padding: '8px 36px 6px',
      letterSpacing: -0.08
    }
  }, header), /*#__PURE__*/React.createElement("div", {
    style: {
      background: bg,
      borderRadius: 26,
      margin: '0 16px',
      overflow: 'hidden'
    }
  }, children));
}

// ─────────────────────────────────────────────────────────────
// Device frame
// ─────────────────────────────────────────────────────────────
function IOSDevice({
  children,
  width = 402,
  height = 874,
  dark = false,
  title,
  keyboard = false
}) {
  return /*#__PURE__*/React.createElement("div", {
    style: {
      width,
      height,
      borderRadius: 48,
      overflow: 'hidden',
      position: 'relative',
      background: dark ? '#000' : '#F2F2F7',
      boxShadow: '0 40px 80px rgba(0,0,0,0.18), 0 0 0 1px rgba(0,0,0,0.12)',
      fontFamily: '-apple-system, system-ui, sans-serif',
      WebkitFontSmoothing: 'antialiased'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      top: 11,
      left: '50%',
      transform: 'translateX(-50%)',
      width: 126,
      height: 37,
      borderRadius: 24,
      background: '#000',
      zIndex: 50
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      top: 0,
      left: 0,
      right: 0,
      zIndex: 10
    }
  }, /*#__PURE__*/React.createElement(IOSStatusBar, {
    dark: dark
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      height: '100%',
      display: 'flex',
      flexDirection: 'column'
    }
  }, title !== undefined && /*#__PURE__*/React.createElement(IOSNavBar, {
    title: title,
    dark: dark
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      overflow: 'auto'
    }
  }, children), keyboard && /*#__PURE__*/React.createElement(IOSKeyboard, {
    dark: dark
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      bottom: 0,
      left: 0,
      right: 0,
      zIndex: 60,
      height: 34,
      display: 'flex',
      justifyContent: 'center',
      alignItems: 'flex-end',
      paddingBottom: 8,
      pointerEvents: 'none'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      width: 139,
      height: 5,
      borderRadius: 100,
      background: dark ? 'rgba(255,255,255,0.7)' : 'rgba(0,0,0,0.25)'
    }
  })));
}

// ─────────────────────────────────────────────────────────────
// Keyboard — iOS 26 liquid glass
// ─────────────────────────────────────────────────────────────
function IOSKeyboard({
  dark = false
}) {
  const glyph = dark ? 'rgba(255,255,255,0.7)' : '#595959';
  const sugg = dark ? 'rgba(255,255,255,0.6)' : '#333';
  const keyBg = dark ? 'rgba(255,255,255,0.22)' : 'rgba(255,255,255,0.85)';

  // special-key icons
  const icons = {
    shift: /*#__PURE__*/React.createElement("svg", {
      width: "19",
      height: "17",
      viewBox: "0 0 19 17"
    }, /*#__PURE__*/React.createElement("path", {
      d: "M9.5 1L1 9.5h4.5V16h8V9.5H18L9.5 1z",
      fill: glyph
    })),
    del: /*#__PURE__*/React.createElement("svg", {
      width: "23",
      height: "17",
      viewBox: "0 0 23 17"
    }, /*#__PURE__*/React.createElement("path", {
      d: "M7 1h13a2 2 0 012 2v11a2 2 0 01-2 2H7l-6-7.5L7 1z",
      fill: "none",
      stroke: glyph,
      strokeWidth: "1.6",
      strokeLinejoin: "round"
    }), /*#__PURE__*/React.createElement("path", {
      d: "M10 5l7 7M17 5l-7 7",
      stroke: glyph,
      strokeWidth: "1.6",
      strokeLinecap: "round"
    })),
    ret: /*#__PURE__*/React.createElement("svg", {
      width: "20",
      height: "14",
      viewBox: "0 0 20 14"
    }, /*#__PURE__*/React.createElement("path", {
      d: "M18 1v6H4m0 0l4-4M4 7l4 4",
      fill: "none",
      stroke: "#fff",
      strokeWidth: "1.8",
      strokeLinecap: "round",
      strokeLinejoin: "round"
    }))
  };
  const key = (content, {
    w,
    flex,
    ret,
    fs = 25,
    k
  } = {}) => /*#__PURE__*/React.createElement("div", {
    key: k,
    style: {
      height: 42,
      borderRadius: 8.5,
      flex: flex ? 1 : undefined,
      width: w,
      minWidth: 0,
      background: ret ? '#08f' : keyBg,
      boxShadow: '0 1px 0 rgba(0,0,0,0.075)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      fontFamily: '-apple-system, "SF Compact", system-ui',
      fontSize: fs,
      fontWeight: 458,
      color: ret ? '#fff' : glyph
    }
  }, content);
  const row = (keys, pad = 0) => /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 6.5,
      justifyContent: 'center',
      padding: `0 ${pad}px`
    }
  }, keys.map(l => key(l, {
    flex: true,
    k: l
  })));
  return /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'relative',
      zIndex: 15,
      borderRadius: 27,
      overflow: 'hidden',
      padding: '11px 0 2px',
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      boxShadow: dark ? '0 -2px 20px rgba(0,0,0,0.09)' : '0 -1px 6px rgba(0,0,0,0.018), 0 -3px 20px rgba(0,0,0,0.012)'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      inset: 0,
      borderRadius: 27,
      backdropFilter: 'blur(12px) saturate(180%)',
      WebkitBackdropFilter: 'blur(12px) saturate(180%)',
      background: dark ? 'rgba(120,120,128,0.14)' : 'rgba(255,255,255,0.25)'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      inset: 0,
      borderRadius: 27,
      boxShadow: dark ? 'inset 1.5px 1.5px 1px rgba(255,255,255,0.15)' : 'inset 1.5px 1.5px 1px rgba(255,255,255,0.7), inset -1px -1px 1px rgba(255,255,255,0.4)',
      border: dark ? '0.5px solid rgba(255,255,255,0.15)' : '0.5px solid rgba(0,0,0,0.06)',
      pointerEvents: 'none'
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 20,
      alignItems: 'center',
      padding: '8px 22px 13px',
      width: '100%',
      boxSizing: 'border-box',
      position: 'relative'
    }
  }, ['"The"', 'the', 'to'].map((w, i) => /*#__PURE__*/React.createElement(React.Fragment, {
    key: i
  }, i > 0 && /*#__PURE__*/React.createElement("div", {
    style: {
      width: 1,
      height: 25,
      background: '#ccc',
      opacity: 0.3
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      textAlign: 'center',
      fontFamily: '-apple-system, system-ui',
      fontSize: 17,
      color: sugg,
      letterSpacing: -0.43,
      lineHeight: '22px'
    }
  }, w)))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexDirection: 'column',
      gap: 13,
      padding: '0 6.5px',
      width: '100%',
      boxSizing: 'border-box',
      position: 'relative'
    }
  }, row(['q', 'w', 'e', 'r', 't', 'y', 'u', 'i', 'o', 'p']), row(['a', 's', 'd', 'f', 'g', 'h', 'j', 'k', 'l'], 20), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 14.25,
      alignItems: 'center'
    }
  }, key(icons.shift, {
    w: 45,
    k: 'shift'
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 6.5,
      flex: 1
    }
  }, ['z', 'x', 'c', 'v', 'b', 'n', 'm'].map(l => key(l, {
    flex: true,
    k: l
  }))), key(icons.del, {
    w: 45,
    k: 'del'
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 6,
      alignItems: 'center'
    }
  }, key('ABC', {
    w: 92.25,
    fs: 18,
    k: 'abc'
  }), key('', {
    flex: true,
    k: 'space'
  }), key(icons.ret, {
    w: 92.25,
    ret: true,
    k: 'ret'
  }))), /*#__PURE__*/React.createElement("div", {
    style: {
      height: 56,
      width: '100%',
      position: 'relative'
    }
  }));
}
Object.assign(window, {
  IOSDevice,
  IOSStatusBar,
  IOSNavBar,
  IOSGlassPill,
  IOSList,
  IOSListRow,
  IOSKeyboard
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/website/ios-frame.jsx", error: String((e && e.message) || e) }); }

__ds_ns.CrestSeal = __ds_scope.CrestSeal;

__ds_ns.Button = __ds_scope.Button;

__ds_ns.IconButton = __ds_scope.IconButton;

__ds_ns.Input = __ds_scope.Input;

__ds_ns.ArticleCard = __ds_scope.ArticleCard;

__ds_ns.Badge = __ds_scope.Badge;

__ds_ns.SectionHeader = __ds_scope.SectionHeader;

__ds_ns.Tag = __ds_scope.Tag;

__ds_ns.IMAGES = __ds_scope.IMAGES;

__ds_ns.CATEGORIES = __ds_scope.CATEGORIES;

__ds_ns.GroupedMasthead = __ds_scope.GroupedMasthead;

__ds_ns.GROUPS = __ds_scope.GROUPS;

})();
