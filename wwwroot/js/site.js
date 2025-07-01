// File: wwwroot/js/site.js (Version 10 - Scope and Timing Fix)

// --- Global Helper Functions ---
// These need to be global because they are called by onclick attributes in HTML

/** Updates the filter count badge */
function updateFilterBadgeCount() {
    const listDiv = document.getElementById('dropdownCategoryList'); // Lookup inside function
    const badge = document.getElementById('filter-badge');           // Lookup inside function
    if (!listDiv || !badge) return;
    try {
        const count = listDiv.querySelectorAll('.dropdown-cat-checkbox:checked').length;
        badge.textContent = count;
        badge.style.display = count > 0 ? '' : 'none';
    } catch (e) { /* Handle error silently or log */ }
}

/** Clears dropdown checkboxes and submits the main filter form */
function clearDropdownFiltersAndSubmit() {
    const listDiv = document.getElementById('dropdownCategoryList'); // Lookup inside function
    const form = document.getElementById('navbarSearchFilterForm'); // Lookup inside function
    if (!form) { console.error("Filter form not found!"); return; }
    if (listDiv) {
        try {
            listDiv.querySelectorAll('.dropdown-cat-checkbox').forEach(cb => cb.checked = false);
            updateFilterBadgeCount(); // Calls global function
            document.getElementById('categoryFilterDropdown')?.classList.remove('show');
        } catch (e) { /* Handle error */ }
    }
    form.submit();
}

/** Clears filters (from page link) and submits the main form */
function clearNavbarFilters() {
    const listDiv = document.getElementById('dropdownCategoryList'); // Lookup inside function
    const form = document.getElementById('navbarSearchFilterForm'); // Lookup inside function
    if (!form) { window.location.href = '/Home/Index'; return; }
    if (listDiv) {
        try {
            listDiv.querySelectorAll('.dropdown-cat-checkbox').forEach(cb => cb.checked = false);
            updateFilterBadgeCount(); // Calls global function
        } catch(e) { /* Handle error */ }
    }
    form.submit();
}

/** Submits search term and categories to the Search page */
function submitSearchToSearchPage() {
    const searchInput = document.getElementById('navbarSearchInput'); // Lookup inside function
    const listDiv = document.getElementById('dropdownCategoryList');    // Lookup inside function
    if (!searchInput) return;
    try {
        const searchTerm = searchInput.value;
        let categoryIds = [];
        if (listDiv) {
            const checkboxes = listDiv.querySelectorAll('.dropdown-cat-checkbox:checked');
            categoryIds = Array.from(checkboxes).map(cb => cb.value);
        }
        const params = new URLSearchParams();
        if (searchTerm) params.append('searchTerm', searchTerm);
        categoryIds.forEach(id => params.append('selectedCategoryIds', id));
        window.location.href = `/Home/Search?${params.toString()}`;
    } catch (e) { /* Handle error */ }
}

/** Populates the category filter dropdown (called from inline script) */
window.initializeCategoryDropdown = function(categoriesData, selectedIdsData) {
    const dropdownCategoryListDiv = document.getElementById('dropdownCategoryList'); // Lookup inside function
    const filterButton = document.getElementById('navbarFilterButton');          // Lookup inside function
    if (!dropdownCategoryListDiv) { if(filterButton) filterButton.disabled = true; return; }

    dropdownCategoryListDiv.innerHTML = '';
    if (!categoriesData || categoriesData.length === 0) { /* Show no categories message */ updateFilterBadgeCount(); if(filterButton) filterButton.disabled = true; return; }
    if(filterButton) filterButton.disabled = false; // Re-enable

    try {
        const selectedIdSet = new Set(selectedIdsData || []);
        categoriesData.forEach(cat => { // Create and append checkboxes...
            const formCheckDiv=document.createElement('div');formCheckDiv.className='form-check';const checkbox=document.createElement('input');checkbox.className='form-check-input dropdown-cat-checkbox';checkbox.type='checkbox';checkbox.value=cat.id;checkbox.id=`dropdown_cat_${cat.id}`;checkbox.name='selectedCategoryIds';if(selectedIdSet.has(String(cat.id)))checkbox.checked=true;const label=document.createElement('label');label.className='form-check-label';label.htmlFor=checkbox.id;label.textContent=cat.text;formCheckDiv.appendChild(checkbox);formCheckDiv.appendChild(label);dropdownCategoryListDiv.appendChild(formCheckDiv);
        });
        updateFilterBadgeCount();
    } catch (e) { /* Handle error */ }
}


// --- Main Execution (Runs after DOM is loaded) ---
document.addEventListener('DOMContentLoaded', function () {
    // console.log("DOM Ready. Initializing site scripts...");

    // --- Get ALL Potential Element References ONCE ---
    const searchInput = document.getElementById('navbarSearchInput');
    const suggestionsContainer = document.getElementById('search-suggestions');
    const filterButton = document.getElementById('navbarFilterButton');
    const categoryDropdown = document.getElementById('categoryFilterDropdown');
    const dropdownCategoryListDiv = document.getElementById('dropdownCategoryList');
    const clearFilterBtnInDropdown = document.getElementById('clearFilterDropdownButton');
    const addBookModalElement = document.getElementById('addBookModal');
    const addBookModalBody = document.getElementById('addBookModalBodyContent');
    const addBookSubmitButton = document.getElementById('createBookSubmitButton');
    const mainForm = document.getElementById('navbarSearchFilterForm'); // Get form ref

    let debounceTimer;

    // --- Initialization Call Placeholder ---
    // Actual initialization is triggered by the inline script in Index.cshtml
    // calling window.initializeCategoryDropdown(...) which now finds its own elements.


    // --- Attach Event Listeners ---

    // Navbar Search/Filter Listeners (Check elements exist before adding)
    if (searchInput && suggestionsContainer && filterButton && categoryDropdown && dropdownCategoryListDiv) {

        searchInput.addEventListener('input', () => {
            clearTimeout(debounceTimer);
            const searchTerm = searchInput.value;
            if (searchTerm.length < 1) { hideAndClearSuggestions(); return; }
            debounceTimer = setTimeout(() => fetchSuggestions(searchTerm), 300);
        });

        filterButton.addEventListener('click', (event) => {
            event.stopPropagation();
            categoryDropdown.classList.toggle('show');
            hideAndClearSuggestions();
        });

        document.addEventListener('click', (event) => {
            // Close Category Dropdown
            if (categoryDropdown.classList.contains('show') && !categoryDropdown.contains(event.target) && !filterButton.contains(event.target)) {
                 categoryDropdown.classList.remove('show');
            }
            // Close Search Suggestions
             if (suggestionsContainer && !suggestionsContainer.contains(event.target) && !searchInput.contains(event.target)) {
                 hideAndClearSuggestions();
            }
        });

        searchInput.addEventListener('keydown', (e) => {
             if (e.key === 'Enter') {
                 if (suggestionsContainer?.style.display === 'block' && suggestionsContainer?.children.length > 0) {
                     e.preventDefault();
                 } else {
                     submitSearchToSearchPage(); // Calls global function
                     e.preventDefault();
                 }
             }
        });

        categoryDropdown.addEventListener('click', (event) => { event.stopPropagation(); });

        // Use event delegation for checkbox changes inside dropdown list
        dropdownCategoryListDiv.addEventListener('change', (event) => {
             if (event.target.type === 'checkbox' && event.target.classList.contains('dropdown-cat-checkbox')) {
                 updateFilterBadgeCount(); // Call global function
                 // Update suggestions based on new filter selection and current term
                 fetchSuggestions(searchInput.value);
             }
        });

        // Listener for the 'Clear' button INSIDE the dropdown
        if (clearFilterBtnInDropdown) {
            clearFilterBtnInDropdown.addEventListener('click', clearDropdownFiltersAndSubmit); // Use global function
        }

    } else {
        // console.warn("Skipping Navbar Search/Filter listener setup - elements missing.");
    }


    // Add Book Modal Listeners (Check elements exist before adding)
    if (addBookModalElement && addBookModalBody) {
        addBookModalElement.addEventListener('shown.bs.modal', loadCreateForm); // Load AFTER shown
        addBookModalElement.addEventListener('hidden.bs.modal', clearCreateForm); // Clear AFTER hidden

        // Listener for the static submit button in the modal footer
        if (addBookSubmitButton) {
            addBookSubmitButton.addEventListener('click', handleCreateFormSubmitViaButton);
        }

        // Listener for focus management when hiding modal (to prevent aria-hidden warning)
        addBookModalElement.addEventListener('hide.bs.modal', function(event) {
            // Regardless of how the modal is closed (Esc, backdrop, button click),
            // try to return focus to the element that originally opened it.
            if (addBookModalTrigger) {
                // console.log("Returning focus to modal trigger button."); // DEBUG
                addBookModalTrigger.focus();
            } else {
                 // Fallback: focus body if trigger isn't found
                 // console.log("Modal trigger not found, focusing body."); // DEBUG
                 document.body.focus(); // May not be ideal, but better than leaving focus trapped
            }
        });

    }
        const modalSubmitButton = document.getElementById('createBookSubmitButton');
        if (modalSubmitButton) {
            modalSubmitButton.addEventListener('click', handleCreateFormSubmitViaButton);
        }
        else {
                // console.warn("Skipping Add Book Modal listener setup - elements missing.");
        }

    // --- Inner Function Definitions (Scoped to DOMContentLoaded) ---
    // These functions are called by the event listeners attached above

    /** Fetches search suggestions */
    async function fetchSuggestions(term) {
        // This function needs dropdownCategoryListDiv to get selected categories
        if (!dropdownCategoryListDiv) { hideAndClearSuggestions(); return; } // Check if list exists
        if (!term || term.length < 1) { hideAndClearSuggestions(); return; } // Check term

        const checkboxes = dropdownCategoryListDiv.querySelectorAll('.dropdown-cat-checkbox:checked');
        const categoryIds = Array.from(checkboxes).map(cb => cb.value);
        const params = new URLSearchParams({ term: term });
        categoryIds.forEach(id => params.append('categoryIds', id));
        const url = `/Home/GetSuggestions?${params.toString()}`;
        try {
            const response = await fetch(url);
            if (!response.ok) throw new Error(`Suggest Error: ${response.status}`);
            displaySuggestions(await response.json(), term);
        } catch (error) { hideAndClearSuggestions(); /* Log error to monitoring */ }
    }

    /** Displays suggestions */
    function displaySuggestions(suggestions, term) {
         if (!suggestionsContainer) return; suggestionsContainer.innerHTML = '';
         if (suggestions?.length > 0) {
            suggestions.forEach(s => {
                const li = document.createElement('li');
                const a = document.createElement('a'); a.href = `/Books/Details/${s.id}`; a.classList.add('dropdown-item');
                const title = s.title;
                if (term && title && title.toLowerCase().startsWith(term.toLowerCase())) {
                    const len = term.length; a.innerHTML = `<strong>${title.substring(0, len)}</strong>${title.substring(len)}`;
                } else { a.textContent = title || "Untitled"; }
                li.appendChild(a); suggestionsContainer.appendChild(li);
            });
            suggestionsContainer.style.display = 'block';
         } else { hideAndClearSuggestions(); }
     }

    /** Hides and clears suggestions */
    function hideAndClearSuggestions() {
        if (suggestionsContainer) { suggestionsContainer.innerHTML = ''; suggestionsContainer.style.display = 'none'; }
    }

    /** Loads the Create Book form into the modal */
    async function loadCreateForm() {
        if (!addBookModalBody) return;
        addBookModalBody.innerHTML = `<div class="text-center p-4"><div class="spinner-border text-secondary" role="status"><span class="visually-hidden">Loading...</span></div><p class="mt-2 text-muted">Loading form...</p></div>`;
        try {
            const response = await fetch('/Books/Create');
            if (!response.ok) throw new Error(`Form Load Error: ${response.status}`);
            addBookModalBody.innerHTML = await response.text();
            const form = addBookModalBody.querySelector('form#createBookForm');
            if (form && window.jQuery?.validator?.unobtrusive) {
                window.jQuery.validator.unobtrusive.parse(form);
            }
        } catch (error) { addBookModalBody.innerHTML = '<div class="alert alert-danger m-0">Could not load form.</div>'; }
    }

    /** Clears the modal body when hidden */
     function clearCreateForm() {
        if (addBookModalBody) addBookModalBody.innerHTML = '';
     }

    /** Handles AJAX submission triggered by the modal's submit button click */
    async function handleCreateFormSubmitViaButton() {
        // Find the form dynamically *inside* the modal body when the button is clicked
        const form = addBookModalBody?.querySelector('form#createBookForm');
        const submitButton = document.getElementById('createBookSubmitButton'); // Get the button ref
        const modalInstance = addBookModalElement ? bootstrap.Modal.getInstance(addBookModalElement) : null; // Get modal instance

        if (!form) { console.error("Create form not found in modal body on submit click."); return; }
        if (!modalInstance) { console.error("Modal instance not found on submit click."); return; }

        if (submitButton) { submitButton.disabled = true; submitButton.innerHTML = '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span> Creating...'; }
        addBookModalBody.querySelector('.alert.ajax-error')?.remove(); // Clear previous errors

        try {
            const formData = new FormData(form);
            // Add token explicitly if not already part of FormData from @Html.AntiForgeryToken()
            const tokenInput = form.querySelector('input[name="__RequestVerificationToken"]');
             if (tokenInput && !formData.has('__RequestVerificationToken')) {
                formData.append('__RequestVerificationToken', tokenInput.value);
            }

            const response = await fetch(form.action, { method: 'POST', body: formData }); // Headers often not needed for FormData POST

            if (response.ok && response.status !== 400 && response.status !== 500) { // SUCCESS
                modalInstance.hide();
                let successMsg = "Book created successfully!";
                try { if (response.headers.get("content-type")?.includes("application/json")) { const result = await response.json(); successMsg = result?.message || successMsg; } } catch { /* Use default */ }
                document.querySelector('.main-content-area')?.insertAdjacentHTML('afterbegin', `<div class="alert alert-success alert-dismissible fade show" role="alert">${successMsg}<button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button></div>`);
                setTimeout(() => { window.location.href = '/Identity/Account/Manage/MyBooks'; }, 1000);

            } else if (response.status === 400) { // VALIDATION ERROR
                addBookModalBody.innerHTML = await response.text();
                const newForm = addBookModalBody.querySelector('form#createBookForm');
                if (newForm && window.jQuery?.validator?.unobtrusive) { window.jQuery.validator.unobtrusive.parse(newForm); }
                const summaryAlert = addBookModalBody.querySelector('.alert[asp-validation-summary="ModelOnly"]');
                if (summaryAlert && summaryAlert.querySelector('ul')?.children.length > 0) { summaryAlert.style.display = 'block'; }
                addBookModalBody.scrollTop = 0;

            } else { // SERVER ERROR
                 throw new Error(`Server error: ${response.status}`);
            }
        } catch (error) { // NETWORK/SCRIPT ERROR
            addBookModalBody.insertAdjacentHTML('afterbegin', `<div class="alert alert-danger m-0 mb-3 ajax-error">Submit error: ${error.message}.</div>`);
            addBookModalBody.scrollTop = 0;
        } finally {
            if (submitButton) { submitButton.disabled = false; submitButton.innerHTML = '<i class="fas fa-check me-1"></i> Create Book'; }
        }
    } // End handleCreateFormSubmitViaButton

}); // End DOMContentLoaded