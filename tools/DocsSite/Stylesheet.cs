namespace Pyrop.DocsSite;

public static class Stylesheet
{
    public const string Text = """
        :root,
        [data-bs-theme=light] {
          --pyrop-brand: #811532;
          --pyrop-brand-dark: #5e0f24;
          --bs-primary: #811532;
          --bs-primary-rgb: 129, 21, 50;
          --bs-primary-text-emphasis: #4d0c1e;
          --bs-primary-bg-subtle: #f3d9e0;
          --bs-primary-border-subtle: #e2a9b8;
          --bs-link-color: #811532;
          --bs-link-color-rgb: 129, 21, 50;
          --bs-link-hover-color: #5e0f24;
          --bs-link-hover-color-rgb: 94, 15, 36;
          --bs-body-color: #1f1f24;
          --bs-body-bg: #ffffff;
          --bs-tertiary-bg: #f6f6f8;
          --bs-border-color: #e2e2e6;
          --bs-code-color: #811532;
          --bs-focus-ring-color: rgba(129, 21, 50, 0.25);
          --pyrop-error: #b42318;
          --k: #811532;
          --t: #1d7a8a;
          --b: #6f42c1;
          --f: #9a5b00;
          --v: #0b6bcb;
          --n: #24479b;
          --d: #8a4b7c;
          --m: #6b6b00;
          --s: #2d7a3a;
          --u: #b35900;
          --c: #6e7781;
        }

        [data-bs-theme=dark] {
          --bs-primary: #e0577b;
          --bs-primary-rgb: 224, 87, 123;
          --bs-primary-text-emphasis: #f2a9bc;
          --bs-primary-bg-subtle: #3a0a17;
          --bs-primary-border-subtle: #6b1a30;
          --bs-link-color: #ff8fab;
          --bs-link-color-rgb: 255, 143, 171;
          --bs-link-hover-color: #ffb3c6;
          --bs-link-hover-color-rgb: 255, 179, 198;
          --bs-body-color: #e6e6ea;
          --bs-body-bg: #16161a;
          --bs-tertiary-bg: #1f1f25;
          --bs-border-color: #2e2e35;
          --bs-code-color: #ff8fab;
          --bs-focus-ring-color: rgba(224, 87, 123, 0.3);
          --pyrop-error: #ff8a80;
          --k: #ff7a9a;
          --t: #4ec9b0;
          --b: #c586c0;
          --f: #dcdcaa;
          --v: #4fc1ff;
          --n: #9cdcfe;
          --d: #e0a3d4;
          --m: #e5c07b;
          --s: #a8d98a;
          --u: #f0a868;
          --c: #8b949e;
        }

        .btn-outline-primary {
          --bs-btn-color: var(--bs-primary);
          --bs-btn-border-color: var(--bs-primary);
          --bs-btn-hover-color: #ffffff;
          --bs-btn-hover-bg: var(--bs-primary);
          --bs-btn-hover-border-color: var(--bs-primary);
          --bs-btn-active-color: #ffffff;
          --bs-btn-active-bg: var(--bs-primary);
          --bs-btn-active-border-color: var(--bs-primary);
          --bs-btn-focus-shadow-rgb: var(--bs-primary-rgb);
        }

        .site-navbar { background: var(--pyrop-brand); }

        [data-bs-theme=dark] .site-navbar { background: var(--pyrop-brand-dark); }

        .site-navbar .navbar-brand {
          display: flex;
          align-items: center;
          gap: 0.5rem;
          font-weight: 700;
        }

        .site-navbar .navbar-brand img {
          width: 1.8rem;
          height: 1.8rem;
          padding: 0.15rem;
          background: #ffffff;
          border-radius: 0.4rem;
        }

        .site-navbar .nav-link.active { text-decoration: underline; text-underline-offset: 0.35em; }

        .docs-content { max-width: 50rem; }

        .docs-pager {
          display: flex;
          gap: 1rem;
          margin-top: 3rem;
          padding-top: 1.5rem;
          border-top: 1px solid var(--bs-border-color);
        }

        .docs-pager-link {
          display: flex;
          flex-direction: column;
          flex: 1 1 0;
          max-width: 50%;
          padding: 0.6rem 1rem;
          border: 1px solid var(--bs-border-color);
          border-radius: 0.5rem;
          text-decoration: none;
          color: var(--bs-body-color);
        }

        .docs-pager-link.next { margin-left: auto; text-align: right; }

        .docs-pager-link:hover, .docs-pager-link:focus-visible { border-color: var(--bs-primary); background: var(--bs-primary-bg-subtle); }

        .docs-pager-caption { font-size: 0.8rem; color: var(--bs-secondary-color); }

        .docs-pager-title { font-weight: 600; color: var(--bs-link-color); }

        .docs-content .docs-pager-title code { color: inherit; background: none; border: 0; padding: 0; }

        .docs-sidebar-toggle {
          padding: 0.25rem 0.5rem;
          border: 1px solid rgba(255, 255, 255, 0.35);
          line-height: 1;
        }

        .docs-sidebar-toggle:hover, .docs-sidebar-toggle:focus-visible { border-color: #ffffff; }

        .docs-sidebar-toggle .navbar-toggler-icon { width: 1.3em; height: 1.3em; }

        .docs-nav { width: 100%; font-size: 0.92rem; }

        .docs-nav ul { margin: 0 0 0 0.25rem; padding-left: 0.6rem; border-left: 1px solid var(--bs-border-color); }

        .docs-nav-section + .docs-nav-section { margin-top: 1.25rem; }

        .docs-nav-heading {
          display: block;
          font-weight: 700;
          font-size: 0.8rem;
          letter-spacing: 0.06em;
          text-transform: uppercase;
          color: var(--bs-body-color);
          text-decoration: none;
          margin-bottom: 0.35rem;
        }

        .docs-nav-group {
          margin: 0.75rem 0 0.2rem;
          font-size: 0.75rem;
          font-weight: 600;
          color: var(--bs-secondary-color);
        }

        .docs-nav-link {
          display: inline-block;
          padding: 0.15rem 0.5rem;
          border-radius: 0.3rem;
          color: var(--bs-body-color);
          text-decoration: none;
        }

        .docs-nav-link:hover, .docs-nav-heading:hover { color: var(--bs-link-color); }

        .docs-nav .active { color: var(--bs-primary-text-emphasis); background: var(--bs-primary-bg-subtle); font-weight: 600; }

        .docs-nav-heading.active { background: none; color: var(--bs-link-color); }

        .docs-nav code { font-size: 0.85em; color: inherit; background: none; border: 0; padding: 0; }

        .docs-nav details > summary { list-style-position: outside; margin-left: 0.9rem; cursor: pointer; }

        .docs-nav details > summary::marker { color: var(--bs-secondary-color); }

        @media (min-width: 992px) {
          .docs-sidebar {
            position: sticky;
            top: 3.6rem;
            height: calc(100vh - 3.6rem);
            overflow-y: auto;
            scrollbar-gutter: stable;
            padding: 1.5rem 0.75rem 2rem 0;
            border-right: 1px solid var(--bs-border-color);
          }
        }

        .docs-content h1 { font-size: 2rem; font-weight: 700; margin: 0.5rem 0 1.25rem; }

        .docs-content h2 { font-size: 1.5rem; font-weight: 700; margin-top: 2.25rem; }

        .docs-content h3 { font-size: 1.2rem; font-weight: 700; margin-top: 1.75rem; }

        .docs-content :not(pre) > code {
          background: var(--bs-tertiary-bg);
          border: 1px solid var(--bs-border-color);
          border-radius: 0.25rem;
          padding: 0.05em 0.3em;
        }

        .docs-content .table { width: auto; }

        .docs-content .table th { background: var(--bs-tertiary-bg); }

        .docs-note {
          padding: 0.5rem 1rem;
          border-left: 0.25rem solid var(--bs-primary);
          background: var(--bs-primary-bg-subtle);
          border-radius: 0 0.375rem 0.375rem 0;
        }

        .docs-note > :last-child { margin-bottom: 0; }

        figure.code { margin: 1rem 0; }

        figure.code pre {
          margin: 0;
          padding: 0.8rem 1rem;
          background: var(--bs-tertiary-bg);
          border: 1px solid var(--bs-border-color);
          border-radius: 0.375rem;
          font-size: 0.9rem;
          line-height: 1.5;
        }

        figure.code pre code { color: var(--bs-body-color); }

        figure.code figcaption {
          font-size: 0.75rem;
          font-weight: 600;
          letter-spacing: 0.05em;
          text-transform: uppercase;
          color: var(--bs-secondary-color);
          margin-bottom: 0.25rem;
        }

        figure.module figcaption, figure.excerpt figcaption {
          text-transform: none;
          letter-spacing: 0;
          font-family: var(--bs-font-monospace);
        }

        figure.example + figure.code, figure.input + figure.code { margin-top: -0.25rem; }

        figure.output pre, figure.input pre { background: transparent; }

        figure.error pre { background: transparent; border-color: var(--pyrop-error); }

        figure.error pre code { color: var(--pyrop-error); }

        .k { color: var(--k); font-weight: 600; }
        .t { color: var(--t); }
        .b { color: var(--b); }
        .f { color: var(--f); }
        .v { color: var(--v); }
        .p { color: var(--n); font-style: italic; }
        .n { color: var(--n); }
        .d { color: var(--d); }
        .m { color: var(--m); }
        .s { color: var(--s); }
        .u { color: var(--u); }
        .c { color: var(--c); font-style: italic; }
        .ph { color: var(--bs-secondary-color); font-style: italic; }

        """;
}
