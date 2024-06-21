export function addAnswerChunk(elem, chunkText) {
    elem = elem.querySelector('.message-text');

    elem.fullText = (elem.fullText || '') + chunkText;
    elem.textContent = elem.fullText;

    // Hide any citation markup
    const citeStartPos = elem.textContent.indexOf('<cite ');
    if (citeStartPos >= 0) {
        const citeEndPos = elem.textContent.indexOf('</cite>');
        if (citeEndPos >= 0) {
            elem.textContent = elem.textContent.substring(0, citeStartPos) + elem.textContent.substring(citeEndPos + 7);
        } else {
            elem.textContent = elem.textContent.substring(0, citeStartPos);
        }
    }

    elem.textContent = elem.textContent.trim();
}
