(() => {
    // One tooltip for the page, pinned to the viewport so a chart's sideways scroll box can't clip it.
    let tooltip = null;
    let active = null;

    const ensureTooltip = () => {
        if (!tooltip) {
            tooltip = document.createElement('div');
            tooltip.className = 'chart-tooltip';
            tooltip.setAttribute('aria-hidden', 'true');
            document.body.appendChild(tooltip);
        }
        return tooltip;
    };

    const hide = () => {
        if (active) active.classList.remove('is-active');
        active = null;
        if (tooltip) tooltip.hidden = true;
    };

    // Built with textContent, never innerHTML: account names and labels are typed by users.
    const fill = (column) => {
        const box = ensureTooltip();
        box.replaceChildren();
        const heading = document.createElement('div');
        heading.className = 'chart-tooltip-label';
        heading.textContent = column.dataset.label;
        box.appendChild(heading);

        let rows = [];
        try { rows = JSON.parse(column.dataset.values || '[]'); } catch { rows = []; }
        for (const row of rows) {
            const line = document.createElement('div');
            line.className = row.color ? 'chart-tooltip-row' : 'chart-tooltip-row chart-tooltip-total';
            const key = document.createElement('span');
            key.className = row.dashed ? 'chart-tooltip-key chart-tooltip-key-dashed' : 'chart-tooltip-key';
            if (row.color) key.style.setProperty('--key-color', row.color);
            const name = document.createElement('span');
            name.className = 'chart-tooltip-name';
            name.textContent = row.name;
            const value = document.createElement('span');
            value.className = 'chart-tooltip-value';
            value.textContent = row.value;
            line.append(key, name, value);
            box.appendChild(line);
        }
        box.hidden = false;
        return box;
    };

    // Beside the point (the pointer, or the column's middle for a keyboard focus), flipped to the
    // left near the right edge and kept fully on screen.
    const place = (box, x, y) => {
        const margin = 8;
        const size = box.getBoundingClientRect();
        let left = x + 14;
        if (left + size.width > window.innerWidth - margin) left = x - size.width - 14;
        let top = y - size.height / 2;
        top = Math.min(Math.max(margin, top), window.innerHeight - size.height - margin);
        box.style.left = `${Math.max(margin, left)}px`;
        box.style.top = `${top}px`;
    };

    const show = (column, x, y) => {
        if (active !== column) {
            hide();
            active = column;
            column.classList.add('is-active');
            fill(column);
        }
        place(tooltip, x, y);
    };

    document.addEventListener('pointermove', (event) => {
        const column = event.target.closest?.('.chart-column');
        if (column) show(column, event.clientX, event.clientY);
        else if (active) hide();
    });

    // A tap on a phone: show that month; a tap anywhere else puts it away.
    document.addEventListener('pointerdown', (event) => {
        const column = event.target.closest?.('.chart-column');
        if (column) show(column, event.clientX, event.clientY);
        else hide();
    });

    document.addEventListener('focusin', (event) => {
        const column = event.target.closest?.('.chart-column');
        if (!column) return;
        const box = column.getBoundingClientRect();
        show(column, box.left + box.width / 2, box.top + box.height / 2);
    });

    document.addEventListener('focusout', (event) => {
        if (event.target.closest?.('.chart-column')) hide();
    });

    document.addEventListener('pointerleave', hide);
    window.addEventListener('scroll', hide, { capture: true, passive: true });
    window.addEventListener('resize', hide);
    document.addEventListener('keydown', (event) => { if (event.key === 'Escape') hide(); });
})();
