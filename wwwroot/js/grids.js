function appendGridTemplate(container, templateId) {
    const template = document.getElementById(templateId);
    $(container).append(template.content.cloneNode(true));
}

window.schoolGrid = {
    rowNumber(container, cell) {
        const firstRow = cell.component.pageIndex() * cell.component.pageSize();
        $(container).text(firstRow + cell.rowIndex + 1);
    },

    cell(container, cell) {
        const gridId = cell.component.element()[0].id;
        appendGridTemplate(container, `${gridId}-${cell.column.name}-${cell.data.Id}`);
    },

    details(container, row) {
        appendGridTemplate(container, `loginHistoryGrid-details-${row.data.Id}`);
    }
};
