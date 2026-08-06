document.addEventListener("click", event => {
    if (!(event.target instanceof Element)) {
        return;
    }

    const activeMenu = event.target.closest("details.profile-menu, details.notification-center");
    document.querySelectorAll("details.profile-menu[open], details.notification-center[open]").forEach(menu => {
        if (menu !== activeMenu) {
            menu.removeAttribute("open");
        }
    });
});

document.addEventListener("keydown", event => {
    if (event.key !== "Escape") {
        return;
    }

    document.querySelectorAll("details.profile-menu[open], details.notification-center[open]").forEach(menu => menu.removeAttribute("open"));
});

window.businessPortal ??= {};
window.businessPortal.positionPicker = (trigger, menu, preferredWidth) => {
    if (!(trigger instanceof HTMLElement) || !(menu instanceof HTMLElement)) {
        return;
    }

    const viewportPadding = 12;
    const gap = 7;
    const triggerRect = trigger.getBoundingClientRect();
    const requestedWidth = Number(preferredWidth) || Math.max(triggerRect.width, 240);
    const width = Math.min(Math.max(triggerRect.width, requestedWidth), window.innerWidth - viewportPadding * 2);

    menu.style.width = `${width}px`;
    menu.style.left = `${Math.min(Math.max(viewportPadding, triggerRect.left), window.innerWidth - width - viewportPadding)}px`;

    const menuHeight = menu.offsetHeight;
    const roomBelow = window.innerHeight - triggerRect.bottom - viewportPadding;
    const roomAbove = triggerRect.top - viewportPadding;
    const openAbove = roomBelow < Math.min(menuHeight, 280) && roomAbove > roomBelow;
    const availableHeight = Math.max(180, openAbove ? roomAbove - gap : roomBelow - gap);

    menu.classList.toggle("opens-above", openAbove);
    menu.style.maxHeight = `${Math.min(menuHeight, availableHeight)}px`;
    menu.style.top = `${openAbove
        ? Math.max(viewportPadding, triggerRect.top - Math.min(menuHeight, availableHeight) - gap)
        : triggerRect.bottom + gap}px`;
};
