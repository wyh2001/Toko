(function() {
    let ticking = false;
    
    function updateScrollState() {
        const isScrolled = window.scrollY > 150;
        document.body.classList.toggle('scrolled', isScrolled);
        ticking = false;
    }
    
    function onScroll() {
        if (!ticking) {
            requestAnimationFrame(updateScrollState);
            ticking = true;
        }
    }
    
    // Listen for scroll events
    window.addEventListener('scroll', onScroll, { passive: true });
    
    updateScrollState();
})();
