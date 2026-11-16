const rightsCapabilities = {
    IsAdd: "CanAdd",
    IsEdit: "CanEdit",
    IsRead: "CanRead",
    IsDelete: "CanDelete",
    IsImport: "CanImport",
    IsExport: "CanExport",
    Approve1: "CanApprove1",
    Approve2: "CanApprove2"
};

window.pageInRole = {
    canEdit({ row }) {
        return row.data.IsLeafNode && Object.values(rightsCapabilities).some(field => row.data[field]);
    },

    prepareEditor(event) {
        const capability = rightsCapabilities[event.dataField];
        if (event.parentType === "dataRow" && capability && !event.row.data[capability]) {
            event.editorOptions.disabled = true;
        }
    },

    save(event) {
        const change = event.changes[0];
        if (!change) {
            return;
        }

        event.cancel = true;
        event.promise = pageInRole.saveRow(event.component, change);
    },

    async saveRow(tree, change) {
        const results = document.getElementById("page-rights-results");
        const error = document.getElementById("rights-save-error");
        const status = document.getElementById("rights-save-status");
        const token = results.querySelector('input[name="__RequestVerificationToken"]').value;
        const store = tree.getDataSource().store();

        error.hidden = true;
        status.textContent = "";
        tree.beginCustomLoading("Saving rights...");

        try {
            const row = await store.byKey(change.key);
            const response = await fetch(results.dataset.saveUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json", RequestVerificationToken: token },
                body: JSON.stringify({
                    ...row,
                    ...change.data,
                    type: Number(results.dataset.rightsType),
                    targetId: Number(results.dataset.targetId)
                })
            });
            const result = response.headers.get("content-type")?.includes("application/json")
                ? await response.json()
                : null;
            if (!response.ok || !result?.rowVersion) {
                throw new Error(result?.message ?? "Could not save rights. Refresh the page and try again.");
            }

            await store.update(change.key, { ...change.data, RowVersion: result.rowVersion });
            tree.cancelEditData();
            await tree.refresh();
            status.textContent = "Rights saved.";
        } catch (failure) {
            error.textContent = failure.message;
            error.hidden = false;
        } finally {
            tree.endCustomLoading();
        }
    }
};

const rightsSearch = document.getElementById("page-rights-search");
const rightsType = document.getElementById("rights-type");
const rightsTarget = document.getElementById("rights-target");
const rightsSearchButton = document.getElementById("rights-search-button");
const optionsStatus = document.getElementById("rights-options-status");
let pendingOptions;

rightsType.addEventListener("change", async () => {
    pendingOptions?.abort();
    const request = new AbortController();
    pendingOptions = request;

    const results = document.getElementById("page-rights-results");
    if (results) {
        results.hidden = true;
    }

    rightsSearchButton.disabled = true;
    rightsTarget.disabled = true;
    rightsTarget.replaceChildren(new Option(
        rightsType.value === "1" ? "Choose user" : rightsType.value === "2" ? "Choose designation" : "Choose user or designation", ""));
    optionsStatus.textContent = "";
    if (!rightsType.value) {
        return;
    }

    optionsStatus.textContent = "Loading options...";
    const url = new URL(rightsSearch.dataset.optionsUrl, window.location.href);
    url.searchParams.set("type", rightsType.value);
    try {
        const response = await fetch(url, { signal: request.signal });
        if (!response.ok) {
            throw new Error();
        }

        const options = await response.json();
        if (request.signal.aborted) {
            return;
        }

        for (const option of options) {
            rightsTarget.add(new Option(option.name, option.id));
        }

        rightsTarget.disabled = options.length === 0;
        rightsSearchButton.disabled = options.length === 0;
        optionsStatus.textContent = options.length === 0 ? "No active entries are available for this type." : "";
    } catch {
        if (!request.signal.aborted) {
            optionsStatus.textContent = "Could not load options. Reload the page and try again.";
        }
    }
});

rightsTarget.addEventListener("change", () => {
    const results = document.getElementById("page-rights-results");
    if (results) {
        results.hidden = rightsType.value !== results.dataset.rightsType
            || rightsTarget.value !== results.dataset.targetId;
    }
});
