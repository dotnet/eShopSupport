// This needs to be a custom element, because we need it to run during prerendering and immediately
// after FluentUI's <loading-theme> custom element initializes. Otherwise there's a flash of wrong styling.
// This also means it has to be a regular script file and not a module loaded via JS interop.

class LoadingThemeInfo extends HTMLElement {
    static getEffectiveTheme() {
        const explicitTheme = document.querySelector('fluent-design-theme').getAttribute('mode');
        switch (explicitTheme) {
            case 'dark':
            case 'light':
                return explicitTheme;
            default:
                // It's using system, or no theme is set so it defaults to system
                return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
        }
    }

    static updateEffectiveThemeClass() {
        document.documentElement.classList.toggle('dark-mode', LoadingThemeInfo.getEffectiveTheme() === 'dark');

    }

    connectedCallback() {
        LoadingThemeInfo.updateEffectiveThemeClass();
    }
}

window.customElements.define('loading-theme-info', LoadingThemeInfo);
window.LoadingThemeInfo = LoadingThemeInfo;

window.scrollToTop = function () {
    window.scrollTo({ top: 0, left: 0, behavior: 'instant' });
}

window.setFocus = function (element) {
    document.getElementsByClassName(element)[0].focus();
}