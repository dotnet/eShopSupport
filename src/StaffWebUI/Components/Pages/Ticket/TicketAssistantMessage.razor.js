import { parseJSON as parsePartialJSON } from '/partial-json-parser.js';

export function addMessageChunk(elem, chunk) {
    elem._messageJson ||= '';
    const messageJson = (elem._messageJson += chunk) || '[]';
    const allSearchResultIds = [];

    parsePartialJSON(messageJson).forEach(message => {
        if (message.searchPhrase) {
            const searchInfo = elem.querySelector('.search-info');
            searchInfo.style.display = 'block';
            searchInfo.textContent = message.searchPhrase;
        }

        if (message.answer) {
            elem.querySelector('.message-text').textContent = message.answer;
        }

        if (message.searchResults) {
            allSearchResultIds.push(...message.searchResults);
        }
        
        if (message.mostRelevantSearchResult && allSearchResultIds) {
            // We only show the reference if the ID actually came from a search result
            // This avoids hallucinated references, and avoids linking to partial IDs when the full ID hasn't yet streamed in
            if (allSearchResultIds.indexOf(message.mostRelevantSearchResult.toString()) >= 0) {
                const referenceLink = elem.querySelector('.reference-link');
                referenceLink.style.display = 'block';
                referenceLink.setAttribute('href', `/manuals/paragraph-${message.mostRelevantSearchResult}`);
                referenceLink.querySelector('.ref-text').textContent = message.mostRelevantSearchQuote || 'Reference';
            }
        }
    });
}
