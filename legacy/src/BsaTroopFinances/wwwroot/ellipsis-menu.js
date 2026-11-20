(() => {
    const close = (menu) => {
        const checkbox = menu.querySelector('.ellipsis-menu-check');
        if (checkbox) checkbox.checked = false;
        unfloat(menu);
    };

    const closeAll = () => document.querySelectorAll('.ellipsis-menu').forEach(close);

    const unfloat = (menu) => {
        const panel = menu.querySelector('.ellipsis-menu-panel');
        if (panel && panel.classList.contains('ellipsis-menu-panel-floating')) {
            panel.classList.remove('ellipsis-menu-panel-floating');
            panel.style.top = panel.style.left = '';
        }
    };

    // A menu inside a .table-scroll would be clipped by the scroll box (overflow can't be
    // horizontal-only), most visibly on a table of one or two short rows. So an opened one is
    // pinned to the viewport instead: beside its toggle, opening leftward like the CSS fallback,
    // and moved up or right as needed to stay fully on screen.
    const float = (menu) => {
        const panel = menu.querySelector('.ellipsis-menu-panel');
        const toggle = menu.querySelector('.ellipsis-menu-toggle');
        if (!panel || !toggle || !menu.closest('.table-scroll')) return;

        panel.classList.add('ellipsis-menu-panel-floating');
        const anchor = toggle.getBoundingClientRect();
        const size = panel.getBoundingClientRect();
        const margin = 8;

        let left = anchor.left - size.width - 4;
        if (left < margin) left = Math.min(anchor.right + 4, window.innerWidth - size.width - margin);
        let top = anchor.top;
        if (top + size.height > window.innerHeight - margin) top = anchor.bottom - size.height;
        top = Math.max(margin, top);

        panel.style.left = `${Math.max(margin, left)}px`;
        panel.style.top = `${top}px`;
    };

    document.addEventListener('change', (event) => {
        if (!event.target.matches('.ellipsis-menu-check')) return;
        const menu = event.target.closest('.ellipsis-menu');
        if (event.target.checked) {
            // One open menu at a time.
            document.querySelectorAll('.ellipsis-menu').forEach((other) => { if (other !== menu) close(other); });
            float(menu);
        } else {
            unfloat(menu);
        }
    });

    // Picking an item closes its menu: the page's own handler still runs (this only unchecks the
    // toggle), and a list that re-renders afterwards can't leave a menu open over another row.
    document.addEventListener('click', (event) => {
        const item = event.target.closest('.ellipsis-menu-item');
        if (item && !item.disabled) {
            setTimeout(() => close(item.closest('.ellipsis-menu')), 0);
        }
    });

    // A pinned menu would drift away from its row on scroll or resize; close it instead.
    window.addEventListener('scroll', closeAll, { capture: true, passive: true });
    window.addEventListener('resize', closeAll);

    // Keyboard/tab navigation: a checkbox-hack menu has no native "blur closes me" behavior, so
    // closing has to be driven manually once focus has actually settled outside the menu.
    document.addEventListener('focusout', (event) => {
        const menu = event.target.closest('.ellipsis-menu');
        if (!menu) return;
        setTimeout(() => {
            if (!menu.contains(document.activeElement)) {
                close(menu);
            }
        }, 0);
    });

    // Mouse/touch: clicking a <label> toggle doesn't always move real DOM focus, so a click
    // anywhere outside the menu needs its own check too.
    document.addEventListener('pointerdown', (event) => {
        document.querySelectorAll('.ellipsis-menu').forEach((menu) => {
            if (!menu.contains(event.target)) {
                close(menu);
            }
        });
    });
})();
