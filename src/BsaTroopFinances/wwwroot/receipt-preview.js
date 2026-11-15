// Shows the receipt a person has chosen in a file input before anything is uploaded: the file is
// read straight from the <input type="file"> (Blazor's InputFile leaves it there, dropped files
// included) through an object URL, so previewing costs no round trip to the server. Used by
// Components/ReceiptPreview.razor.
window.receiptPreview = (() => {
    const urls = new Map();

    function clear(containerId) {
        const url = urls.get(containerId);
        if (url) {
            URL.revokeObjectURL(url);
            urls.delete(containerId);
        }
        const container = document.getElementById(containerId);
        if (container) {
            container.replaceChildren();
        }
    }

    function render(inputId, containerId) {
        clear(containerId);
        const input = document.getElementById(inputId);
        const container = document.getElementById(containerId);
        const file = input && input.files && input.files[0];
        if (!container || !file) {
            return;
        }

        const url = URL.createObjectURL(file);
        urls.set(containerId, url);

        const isPdf = file.type === 'application/pdf' || /\.pdf$/i.test(file.name);
        if (isPdf) {
            const frame = document.createElement('iframe');
            frame.src = url;
            frame.title = 'Receipt preview: ' + file.name;
            container.appendChild(frame);

            // Some phone browsers (Android Chrome in particular) won't draw a PDF inline.
            const link = document.createElement('a');
            link.href = url;
            link.target = '_blank';
            link.rel = 'noopener';
            link.className = 'receipt-preview-open';
            link.textContent = 'Open the PDF in a new tab';
            container.appendChild(link);
        } else {
            const img = document.createElement('img');
            img.src = url;
            img.alt = 'Receipt preview: ' + file.name;
            container.appendChild(img);
        }
    }

    return { render, clear };
})();
