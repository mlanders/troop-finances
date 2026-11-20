// Closes the admin menu (the ☰ drop-down, or the phone's More panel) once it has done its job.
// The menu is a CSS checkbox (AdminLayout.razor), and Blazor's enhanced navigation keeps the page's
// DOM between pages, so without this it would still be open on the page a link led to.
(function () {
    'use strict';

    function closeMenu() {
        var check = document.getElementById('adminMenuCheck');
        if (check && check.checked) {
            check.checked = false;
        }
    }

    // A link or button in the menu, or a tab in the phone tab bar (other than More itself).
    document.addEventListener('click', function (e) {
        var target = e.target.closest && e.target.closest('.admin-header-content a, .admin-tabbar a');
        if (target) {
            closeMenu();
        }
    });

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') {
            closeMenu();
        }
    });

    window.addEventListener('load', function () {
        if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
            window.Blazor.addEventListener('enhancedload', closeMenu);
        }
    });
})();
