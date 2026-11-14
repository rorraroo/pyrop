namespace Pyrop.DocsSite;

public static class SearchScript
{
    public const string Text = """
        (function () {
          var form = document.querySelector('.docs-search');
          if (!form) { return; }
          var input = form.querySelector('input');
          var list = form.querySelector('.docs-search-results');
          var root = form.getAttribute('data-root');
          var index = null, loading = false, results = [], active = -1;
          form.hidden = false;

          function load() {
            if (index || loading) { return; }
            loading = true;
            var script = document.createElement('script');
            script.src = root + 'search-index.js';
            script.onload = function () { index = window.pyropSearchIndex || []; if (document.activeElement === input) { show(); } };
            script.onerror = function () { loading = false; };
            document.head.appendChild(script);
          }

          function wordsOf(query) { return query.toLowerCase().split(/\s+/).filter(Boolean); }

          function count(text, word) {
            var n = 0, at = text.indexOf(word);
            while (at >= 0 && n < 20) { n++; at = text.indexOf(word, at + word.length); }
            return n;
          }

          function search(words) {
            var whole = words.join(' '), found = [];
            index.forEach(function (entry) {
              var heading = entry.h.toLowerCase(), page = entry.p.toLowerCase(), text = entry.x.toLowerCase(), score = 0;
              var headingWords = heading.split(/[^a-z0-9_]+/);
              for (var i = 0; i < words.length; i++) {
                var inHeading = heading.indexOf(words[i]) >= 0, inPage = page.indexOf(words[i]) >= 0, inText = count(text, words[i]);
                if (!inHeading && !inPage && !inText) { return; }
                score += (inHeading ? 20 : 0) + (headingWords.indexOf(words[i]) >= 0 ? 30 : 0) + (inPage ? 5 : 0) + inText;
              }
              if (heading === whole) { score += 1000; } else if (heading.indexOf(whole) === 0) { score += 50; }
              found.push({ entry: entry, score: score });
            });
            found.sort(function (a, b) { return b.score - a.score; });
            return found.slice(0, 10).map(function (item) { return item.entry; });
          }

          function escape(text) {
            return text.replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; });
          }

          function marked(text, pattern) {
            return text.split(pattern).map(function (part, i) { return i % 2 ? '<mark>' + escape(part) + '</mark>' : escape(part); }).join('');
          }

          function excerpt(text, pattern) {
            var at = text.search(pattern);
            if (at < 0) { at = 0; }
            var start = Math.max(0, at - 40);
            if (start > 0) { var space = text.indexOf(' ', start); if (space >= 0 && space < at) { start = space + 1; } }
            var end = Math.min(text.length, start + 140);
            if (end < text.length) { var last = text.lastIndexOf(' ', end); if (last > at) { end = last; } }
            return (start > 0 ? '… ' : '') + text.slice(start, end) + (end < text.length ? ' …' : '');
          }

          function show() {
            var words = wordsOf(input.value);
            if (!words.length) { close(); return; }
            if (!index) { load(); return; }
            results = search(words);
            active = results.length ? 0 : -1;
            var pattern = new RegExp('(' + words.map(function (w) { return w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }).join('|') + ')', 'i');
            list.innerHTML = results.length
              ? results.map(function (entry, i) {
                  return '<li role="option" id="docs-search-' + i + '"><a href="' + escape(root + entry.u) + '" tabindex="-1">'
                    + '<span class="docs-search-heading">' + marked(entry.h, pattern) + '</span>'
                    + (entry.h !== entry.p ? '<span class="docs-search-page">' + escape(entry.p) + '</span>' : '')
                    + '<span class="docs-search-text">' + marked(excerpt(entry.x, pattern), pattern) + '</span></a></li>';
                }).join('')
              : '<li class="docs-search-none">Nothing matches.</li>';
            list.hidden = false;
            input.setAttribute('aria-expanded', 'true');
            highlight();
          }

          function highlight() {
            var items = list.querySelectorAll('[role=option]');
            for (var i = 0; i < items.length; i++) {
              items[i].classList.toggle('active', i === active);
              items[i].setAttribute('aria-selected', i === active ? 'true' : 'false');
            }
            if (active >= 0) {
              input.setAttribute('aria-activedescendant', 'docs-search-' + active);
              items[active].scrollIntoView({ block: 'nearest' });
            } else {
              input.removeAttribute('aria-activedescendant');
            }
          }

          function close() {
            list.hidden = true;
            list.innerHTML = '';
            results = [];
            active = -1;
            input.setAttribute('aria-expanded', 'false');
            input.removeAttribute('aria-activedescendant');
          }

          function open(i) {
            if (i < 0 || i >= results.length) { return; }
            var target = root + results[i].u;
            close();
            input.blur();
            location.href = target;
          }

          input.addEventListener('focus', function () { load(); show(); });
          input.addEventListener('input', show);
          input.addEventListener('keydown', function (event) {
            if (event.key === 'ArrowDown' && results.length) { event.preventDefault(); active = (active + 1) % results.length; highlight(); }
            else if (event.key === 'ArrowUp' && results.length) { event.preventDefault(); active = (active - 1 + results.length) % results.length; highlight(); }
            else if (event.key === 'Enter') { event.preventDefault(); open(active); }
            else if (event.key === 'Escape') { event.preventDefault(); if (list.hidden) { input.blur(); } else { close(); } }
          });
          form.addEventListener('submit', function (event) { event.preventDefault(); });
          list.addEventListener('mousedown', function (event) { event.preventDefault(); });
          list.addEventListener('click', function (event) {
            var item = event.target.closest('[role=option]');
            if (item) { event.preventDefault(); open(Array.prototype.indexOf.call(list.children, item)); }
          });
          input.addEventListener('blur', close);
          document.addEventListener('keydown', function (event) {
            var target = event.target;
            if (event.key !== '/' || event.ctrlKey || event.metaKey || event.altKey) { return; }
            if (target.closest && target.closest('input, textarea, select, [contenteditable]')) { return; }
            event.preventDefault();
            input.focus();
            input.select();
          });
        })();

        """;
}
