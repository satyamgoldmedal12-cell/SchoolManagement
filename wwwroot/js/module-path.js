(() => {
    const leaf = document.getElementById("IsLeafNode");
    const leafFields = document.getElementById("LeafFields");
    const parent = document.getElementById("HierarchyId");
    const name = document.getElementById("PageName");
    const preview = document.getElementById("FullPathPreview");
    const folders = new Map(Array.from(parent.options)
        .filter(option => option.value !== "")
        .map(option => [option.value, option.dataset]));

    function showParentLevel() {
        const selectedId = parent.value;
        const selectedFolder = folders.get(selectedId);
        const options = [new Option("Root level", "")];

        if (selectedFolder) {
            const previousFolder = folders.get(selectedFolder.parentId);
            if (previousFolder) {
                options.push(new Option("Back to " + previousFolder.pageName, selectedFolder.parentId));
            }
            options.push(new Option(selectedFolder.pageName + " (selected parent)", selectedId));
        }

        for (const [id, folder] of folders) {
            const folderParentId = folders.has(folder.parentId) ? folder.parentId : "";
            if (folderParentId === selectedId && id !== selectedId) {
                options.push(new Option(folder.pageName, id));
            }
        }

        parent.replaceChildren(...options);
        parent.value = selectedId;
    }

    function refresh() {
        leafFields.hidden = !leaf.checked;
        leafFields.disabled = !leaf.checked;
        const parentPath = folders.get(parent.value)?.parentPath;
        preview.value = [parentPath, name.value.trim()].filter(Boolean).join(" >> ");
    }

    leaf.addEventListener("change", refresh);
    parent.addEventListener("change", () => {
        showParentLevel();
        refresh();
    });
    name.addEventListener("input", refresh);
    showParentLevel();
    refresh();
})();
