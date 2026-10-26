// Shows only the fields that belong to the chosen account type. The server validates the same rules, so this is a
// convenience, not a security control.
(function () {
  var select = document.getElementById('account-type');
  var sections = document.querySelectorAll('[data-account-section]');
  if (!select) return;

  function update() {
    sections.forEach(function (section) {
      var visible = section.getAttribute('data-account-section') === select.value;
      section.hidden = !visible;
      section.disabled = !visible;
    });
  }

  select.addEventListener('change', update);
  update();
})();
