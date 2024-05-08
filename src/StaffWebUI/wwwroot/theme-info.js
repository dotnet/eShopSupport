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


window.customElements.define('preserve-scroll-pos', class extends HTMLElement {
    connectedCallback() {
        const previousInfo = sessionStorage.getItem('scroll-pos');
        if (previousInfo) {
            const { pos, height } = JSON.parse(previousInfo);
            const child = document.createElement('div');
            child.style.height = `${height}px`;
            child.style.width = '10px';
            child.style.position = 'absolute';
            child.style.top = '0px';
            this.appendChild(child);
            
            setTimeout(function () {
                const scrollDetector = document.querySelector('.tickets-grid tbody div:last-child');
                scrollDetector.style.height = `${height}px`;
            }, 0);
            

            document.querySelector('html').style.scrollBehavior = 'auto';
            window.scrollTo(0, pos);
        }

        this.listener = () => {
            const info = { pos: window.scrollY, height: document.body.offsetHeight };
            sessionStorage.setItem('scroll-pos', JSON.stringify(info));
        };
        window.addEventListener('scroll', this.listener);
    }

    disconnectedCallback() {
        window.removeEventListener('scroll', this.listener);
    }
});
