(() => {
    const picker = document.getElementById("MenuIconPicker");
    if (!picker) return;

    const iconLabels = new Map(Object.entries(JSON.parse(picker.dataset.icons)));
    const iconKeys = [...iconLabels.keys()];
    const savedIcon = document.getElementById("PageSystemName");
    const selected = document.getElementById("SelectedMenuIcon");
    const search = document.getElementById("MenuIconSearch");
    const choices = document.getElementById("MenuIconChoices");
    const previous = document.getElementById("PreviousMenuIcons");
    const next = document.getElementById("NextMenuIcons");
    const count = document.getElementById("MenuIconCount");
    const pageSize = 48;
    let matches = iconKeys;
    let page = 0;

    function glyph(cssClass) {
        const icon = document.createElement("i");
        icon.className = cssClass;
        icon.setAttribute("aria-hidden", "true");
        return icon;
    }

    function displayName(key) {
        const name = iconLabels.get(key);
        if (name) {
            return name;
        }

        if (key === picker.dataset.legacyIcon && picker.dataset.legacyIconLabel) {
            return picker.dataset.legacyIconLabel;
        }

        return key.replace(/[-_]+/g, " ").replace(/\b\w/g, letter => letter.toUpperCase());
    }

    function showSelection() {
        const key = savedIcon.value;
        selected.replaceChildren();
        const cssClass = iconLabels.has(key)
            ? key
            : key === picker.dataset.legacyIcon ? picker.dataset.legacyIconClass : null;
        if (cssClass) {
            const description = document.createElement("span");
            description.className = "visually-hidden";
            description.textContent = "Selected icon: " + displayName(key);
            selected.append(glyph(cssClass), description);
        } else {
            selected.textContent = key ? "Icon selected" : "Select an icon";
        }
    }

    function selectIcon(key) {
        savedIcon.value = key;
        showSelection();
        for (const button of choices.querySelectorAll("button")) {
            const active = button.value === key;
            button.classList.toggle("active", active);
            button.setAttribute("aria-pressed", String(active));
        }
    }

    function render() {
        const start = page * pageSize;
        const visible = matches.slice(start, start + pageSize);
        choices.replaceChildren();
        choices.scrollLeft = 0;

        for (const key of visible) {
            const button = document.createElement("button");
            button.type = "button";
            button.value = key;
            button.className = "btn btn-sm btn-outline-primary menu-icon-choice";
            button.setAttribute("aria-label", displayName(key));
            const active = key === savedIcon.value;
            button.classList.toggle("active", active);
            button.setAttribute("aria-pressed", String(active));
            button.append(glyph(key));
            button.addEventListener("click", () => selectIcon(key));
            choices.append(button);
        }

        previous.disabled = page === 0;
        next.disabled = start + pageSize >= matches.length;
        count.textContent = matches.length
            ? (start + 1) + "–" + (start + visible.length) + " of " + matches.length + " icons"
            : "No icons found";
    }

    search.addEventListener("input", () => {
        const terms = search.value.toLowerCase().split(/[\s-]+/).filter(Boolean);
        matches = iconKeys.filter(key => {
            const searchable = key + " " + iconLabels.get(key).toLowerCase();
            return terms.every(term => searchable.includes(term));
        });
        page = 0;
        render();
    });
    previous.addEventListener("click", () => {
        page--;
        render();
    });
    next.addEventListener("click", () => {
        page++;
        render();
    });

    showSelection();
    render();
})();
