document.addEventListener('DOMContentLoaded', function () {
    const searchInput = document.getElementById('navbarSearchInput');
    const suggestionsContainer = document.getElementById('search-suggestions');
    let debounceTimer;

    if (searchInput && suggestionsContainer) {
        searchInput.addEventListener('input', function () {
            clearTimeout(debounceTimer);
            const searchTerm = searchInput.value;

            if (searchTerm.length < 1) { // Minimum length check (sync with backend)
                suggestionsContainer.innerHTML = ''; // Clear suggestions
                suggestionsContainer.style.display = 'none';
                return;
            }

            debounceTimer = setTimeout(() => {
                fetchSuggestions(searchTerm);
            }, 300); // Debounce time in ms (e.g., 300ms)
        });

        // Hide suggestions when clicking outside
        document.addEventListener('click', function (event) {
            if (!searchInput.contains(event.target) && !suggestionsContainer.contains(event.target)) {
                suggestionsContainer.style.display = 'none';
            }
        });
         // Optional: Hide on input blur (might interfere with clicking suggestion)
        // searchInput.addEventListener('blur', function() {
        //    setTimeout(() => { suggestionsContainer.style.display = 'none'; }, 150); // Delay allows click
        // });
         // Show on focus if there's already text? Maybe not needed.
         // searchInput.addEventListener('focus', function() { ... });

    } else {
        console.error("Search input or suggestions container not found.");
    }

    async function fetchSuggestions(term) {
        // Important: Encode the search term for the URL
        const encodedTerm = encodeURIComponent(term);
        // Construct the URL to your API endpoint
        const url = `/Home/GetSuggestions?term=${encodedTerm}`;

        try {
            const response = await fetch(url);
            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }
            const suggestions = await response.json();
            displaySuggestions(suggestions);

        } catch (error) {
            console.error('Error fetching suggestions:', error);
            suggestionsContainer.innerHTML = ''; // Clear on error
            suggestionsContainer.style.display = 'none';
        }
    }

    function displaySuggestions(suggestions) {
        // Clear previous suggestions
        suggestionsContainer.innerHTML = '';

        if (suggestions && suggestions.length > 0) {
            suggestions.forEach(suggestion => {
                const listItem = document.createElement('li');
                const link = document.createElement('a');
                // Make sure the URL points to your book details page
                link.href = `/Books/Details/${suggestion.id}`;
                link.classList.add('dropdown-item');
                link.textContent = suggestion.title;
                // Optional: Highlight the matched part (more complex)
                // link.innerHTML = suggestion.title.replace(new RegExp(`^${searchInput.value}`, 'i'), `<strong>$&</strong>`);

                listItem.appendChild(link);
                suggestionsContainer.appendChild(listItem);
            });
            suggestionsContainer.style.display = 'block'; // Show container
        } else {
            // Optionally show "No results" message, or just hide
             // const noResultItem = document.createElement('li');
             // noResultItem.innerHTML = `<span class="dropdown-item text-muted disabled">No results found</span>`;
             // suggestionsContainer.appendChild(noResultItem);
             // suggestionsContainer.style.display = 'block';
            suggestionsContainer.style.display = 'none'; // Hide if empty
        }
    }
});