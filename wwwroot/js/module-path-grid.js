window.modulePathGridCells = {
    pageName(container, cell) {
        const element = $(container);
        if (cell.data.IconClass) {
            $("<i>").addClass(cell.data.IconClass + " me-1").attr("aria-hidden", "true").appendTo(element);
        }
        element.append(document.createTextNode(cell.value ?? ""));
    },

    actions(container, cell) {
        const element = $(container).addClass("text-nowrap");
        $("<a>").attr("href", cell.data.EditUrl)
            .addClass("btn btn-sm btn-outline-primary me-1").text("Edit").appendTo(element);
        $("<a>").attr("href", cell.data.DeleteUrl)
            .addClass("btn btn-sm btn-outline-danger").text("Delete").appendTo(element);
    }
};
