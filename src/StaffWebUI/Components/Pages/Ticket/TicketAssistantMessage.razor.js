import { parseJSON as parsePartialJSON } from '/partial-json-parser.js';

export function addMessageChunk(elem, chunk) {
    elem._messageJson ||= '';
    const messageJson = (elem._messageJson += chunk) || '[]';
    const allSearchResultsById = new Map();

    parsePartialJSON(messageJson).forEach(message => {
        if (typeof message === 'string') {
            // This is not the expected format (we expected an object).
            // It might be an error message, so display it.
            elem.querySelector('.message-text').textContent = message;
            return;
        }

        if (message.searchPhrase) {
            const searchInfo = elem.querySelector('.search-info');
            searchInfo.style.display = 'block';
            searchInfo.textContent = message.searchPhrase;
        }

        if (message.answer) {
            elem.querySelector('.message-text').textContent = message.answer;
        }

        if (message.searchResults) {
            message.searchResults.forEach(result => allSearchResultsById.set(result.id, result));
        }
        
        if (message.mostRelevantSearchResultId) {
            // We only show the reference if the ID actually came from a search result
            // This avoids hallucinated references, and avoids linking to partial IDs when the full ID hasn't yet streamed in
            const searchResult = allSearchResultsById.get(message.mostRelevantSearchResultId.toString());
            if (searchResult) {
                const referenceLink = elem.querySelector('.reference-link');
                referenceLink.style.display = 'flex';
                referenceLink.setAttribute('href', `manual.html?file=${searchResult.productId}.pdf&page=${searchResult.pageNumber}&search=${encodeURIComponent(message.mostRelevantSearchQuote)}`);
                referenceLink.querySelector('.ref-text').textContent = message.mostRelevantSearchQuote || 'Reference';
            }
        }
        
        if (message.isAddressedToCustomerByName) {
            const suggestedReplyField = elem.querySelector('[name=suggested-reply]');
            if (suggestedReplyField.value !== message.answer) {
                suggestedReplyField.value = message.answer;
                suggestedReplyField.dispatchEvent(new Event('change', { bubbles: true }));
            }
        }
    });
}
