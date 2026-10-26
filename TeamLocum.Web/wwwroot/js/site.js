// "Go back" links keep a real href for people without scripts and use history when it exists.
document.addEventListener('click', function (event) {
  var link = event.target.closest('[data-history-back]');
  if (link && window.history.length > 1) {
    event.preventDefault();
    window.history.back();
  }
});
