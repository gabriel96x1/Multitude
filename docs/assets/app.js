// Copy buttons + reveal on scroll. No dependencies.
(function () {
  'use strict';

  document.querySelectorAll('[data-copy]').forEach(function (button) {
    button.addEventListener('click', function () {
      var target = document.getElementById(button.getAttribute('data-copy'));
      if (!target) return;
      var text = target.innerText.trim();

      var done = function (ok) {
        button.textContent = ok ? 'copied' : 'error';
        button.setAttribute('data-copied', ok ? 'true' : 'false');
        window.setTimeout(function () {
          button.textContent = button.getAttribute('data-label') || 'copy';
          button.removeAttribute('data-copied');
        }, 1600);
      };

      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).then(function () { done(true); }, function () { done(false); });
        return;
      }

      var area = document.createElement('textarea');
      area.value = text;
      area.setAttribute('readonly', '');
      area.style.position = 'fixed';
      area.style.opacity = '0';
      document.body.appendChild(area);
      area.select();
      try { done(document.execCommand('copy')); } catch (error) { done(false); }
      document.body.removeChild(area);
    });
  });

}());
