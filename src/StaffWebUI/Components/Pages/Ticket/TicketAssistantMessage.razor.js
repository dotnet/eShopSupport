window.customElements.define('assistant-message', class extends HTMLElement {
    static observedAttributes = ['message'];

    connectedCallback() {
        this.textContent = this.getAttribute('message') || '...';
    }

    // Whenever the message attribute changes, update the text content of the element
    attributeChangedCallback(name, oldValue, newValue) {
        if (name === 'message') {
            this.textContent = newValue;
        }
    }
});
