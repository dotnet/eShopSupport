import { parseJSON as parsePartialJSON } from '/partial-json-parser.js';

window.customElements.define('assistant-message', class extends HTMLElement {
    static observedAttributes = ['message'];

    _searchInfo = document.createElement('div');
    _messageText = document.createElement('div');
    _referenceLink = document.createElement('a');
    _referenceLinkText = document.createElement('span');

    connectedCallback() {
        this.appendChild(this._searchInfo);
        this.appendChild(this._messageText);
        this.appendChild(this._referenceLink);
        this._referenceLink.appendChild(this._referenceLinkText);
        this._searchInfo.classList.add('search-info');
        this._referenceLink.classList.add('reference-link');
        this._referenceLinkText.classList.add('ref-text');
        this._updateDisplay();
    }

    // Whenever the message attribute changes, update the text content of the element
    attributeChangedCallback(name, oldValue, newValue) {
        if (name === 'message') {
            this._updateDisplay();
        }
    }

    _updateDisplay() {
        this._searchInfo.style.display = 'none';
        this._messageText.style.display = 'none';
        this._referenceLink.style.display = 'none';

        // The double JSON encoding is suboptimal but avoids escaping issues in HTML attributes
        // An alternative would be emitting the text to a <template> element inside this custom element
        // but then we don't get the benefit of notifications whenever the attribute changes
        const messageJson = JSON.parse(this.getAttribute('message') || '""');
        let hasContent = false;
        const validReferenceIds = [];

        messageJson && parsePartialJSON(messageJson).forEach(message => {
            if (message.searchPhrase) {
                hasContent = true;
                this._searchInfo.style.display = 'block';
                this._searchInfo.textContent = message.searchPhrase;
            }

            if (message.answer) {
                hasContent = true;
                this._messageText.style.display = 'block';
                this._messageText.textContent = message.answer;
            }

            if (message.searchResults) {
                validReferenceIds.push(...message.searchResults);
            }

            if (message.mostRelevantSearchResult) {
                // We only show the reference if the ID actually came from a search result
                // This avoids hallucinated references, and avoids linking to partial IDs when the full ID hasn't yet streamed in
                if (validReferenceIds.indexOf(message.mostRelevantSearchResult.toString()) >= 0) {
                    hasContent = true;
                    this._referenceLink.style.display = 'block';
                    this._referenceLink.setAttribute('href', `/manuals/paragraph-${message.mostRelevantSearchResult}`);
                    this._referenceLinkText.textContent = message.mostRelevantSearchQuote || 'Reference';
                }
            }
        });

        if (!hasContent) {
            this._messageText.textContent = '...';
            this._messageText.style.display = 'block';
        }
    }
});
