(() => {
    const parseAmount = (text, allowNegative) => {
        const sign = allowNegative && text.includes('-') ? -1 : 1;
        const decimalIndex = text.lastIndexOf('.');
        const fractional = decimalIndex >= 0 ? (text.slice(decimalIndex + 1).match(/\d/g) || []).length : 0;
        const explicit = decimalIndex >= 0 && fractional > 0 && fractional <= 2;
        if (explicit) {
            const value = Number(text.replace(/[$,]/g, ''));
            if (Number.isFinite(value)) return allowNegative ? sign * Math.abs(value) : Math.max(0, value);
        }

        const digits = (text.match(/\d/g) || []).join('').slice(0, 10);
        const magnitude = digits ? Number(digits) / 100 : 0;
        return allowNegative ? sign * magnitude : magnitude;
    };

    const format = (value) => value.toLocaleString('en-US', {
        style: 'currency',
        currency: 'USD',
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    });

    const countDigits = (text) => (text.match(/\d/g) || []).length;

    const caretAfterDigits = (text, digitCount) => {
        if (digitCount <= 0) return 1;
        let seen = 0;
        for (let index = 0; index < text.length; index++) {
            if (/\d/.test(text[index])) {
                seen++;
                if (seen === digitCount) return index + 1;
            }
        }
        return text.length;
    };

    const refresh = (input) => {
        const original = input.value;
        const allowNegative = input.hasAttribute('data-allow-negative');
        const selectionStart = input.selectionStart ?? original.length;
        const digitsBeforeCaret = countDigits(original.slice(0, selectionStart));
        const formatted = format(parseAmount(original, allowNegative));
        input.value = formatted;
        const caret = caretAfterDigits(formatted, digitsBeforeCaret);
        input.setSelectionRange(caret, caret);
    };

    const wire = (root = document) => {
        root.querySelectorAll('input[data-money-input]').forEach((input) => {
            if (input.dataset.amountWired === 'true') return;
            input.dataset.amountWired = 'true';
            input.addEventListener('input', () => refresh(input));
            refresh(input);
        });
    };

    wire();
    new MutationObserver(() => wire()).observe(document.body, { childList: true, subtree: true });
})();
