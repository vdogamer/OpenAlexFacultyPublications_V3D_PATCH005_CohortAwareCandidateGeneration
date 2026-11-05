(() => {
    const navigation = document.querySelector(".primary-nav");
    const toggle = document.querySelector(".nav-toggle");
    if (!navigation || !toggle) return;

    const groups = [...navigation.querySelectorAll(".nav-group")];
    const mobile = window.matchMedia("(max-width: 840px)");
    const closeGroups = (except) => groups.forEach(group => {
        if (group !== except) group.open = false;
    });
    const closeNavigation = () => {
        toggle.setAttribute("aria-expanded", "false");
        navigation.classList.remove("is-open");
        closeGroups();
    };

    navigation.dataset.enhanced = "true";
    toggle.hidden = false;
    toggle.addEventListener("click", () => {
        const open = toggle.getAttribute("aria-expanded") !== "true";
        toggle.setAttribute("aria-expanded", String(open));
        navigation.classList.toggle("is-open", open);
        if (!open) closeGroups();
    });
    groups.forEach(group => {
        group.addEventListener("toggle", () => {
            if (group.open) closeGroups(group);
        });
        group.addEventListener("focusout", event => {
            if (!mobile.matches && event.relatedTarget instanceof Node && !group.contains(event.relatedTarget)) {
                group.open = false;
            }
        });
    });
    document.addEventListener("click", event => {
        if (!navigation.contains(event.target) && !toggle.contains(event.target)) closeNavigation();
    });
    document.addEventListener("keydown", event => {
        if (event.key !== "Escape") return;
        const group = groups.find(item => item.open && item.contains(document.activeElement));
        if (group) {
            group.open = false;
            group.querySelector("summary").focus();
        } else if (mobile.matches && toggle.getAttribute("aria-expanded") === "true") {
            closeNavigation();
            toggle.focus();
        } else {
            closeGroups();
        }
    });
    mobile.addEventListener("change", closeNavigation);
})();
