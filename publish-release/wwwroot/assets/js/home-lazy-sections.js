document.addEventListener('DOMContentLoaded', function () {
    const lazySections = Array.from(document.querySelectorAll('.home-lazy-section[data-home-section-url]'));

    if (lazySections.length === 0) {
        return;
    }

    const refreshAnimations = function () {
        if (window.AOS && typeof window.AOS.refreshHard === 'function') {
            window.AOS.refreshHard();
            return;
        }

        if (window.AOS && typeof window.AOS.refresh === 'function') {
            window.AOS.refresh();
        }
    };

    const markAsFailed = function (section, message) {
        section.classList.add('home-lazy-section--failed');
        section.innerHTML = [
            '<div class="home-lazy-section__placeholder home-lazy-section__placeholder--error" role="alert">',
            '<i class="bi bi-exclamation-triangle text-warning"></i>',
            '<span>', message, '</span>',
            '</div>'
        ].join('');
    };

    const loadSection = async function (section) {
        if (section.dataset.homeSectionLoaded === 'true' || section.dataset.homeSectionLoading === 'true') {
            return;
        }

        const sectionUrl = section.dataset.homeSectionUrl;
        if (!sectionUrl) {
            return;
        }

        section.dataset.homeSectionLoading = 'true';

        try {
            const response = await fetch(sectionUrl, {
                headers: {
                    'X-Requested-With': 'XMLHttpRequest'
                },
                credentials: 'same-origin'
            });

            if (!response.ok) {
                throw new Error(`Lazy section request failed with status ${response.status}`);
            }

            const html = await response.text();
            section.innerHTML = html;
            section.dataset.homeSectionLoaded = 'true';
            section.classList.add('home-lazy-section--loaded');
            refreshAnimations();
        } catch (error) {
            console.warn('Unable to load home section:', error);
            markAsFailed(section, 'Chưa tải được nội dung. Vui lòng làm mới trang để thử lại.');
        } finally {
            section.dataset.homeSectionLoading = 'false';
        }
    };

    if (!('IntersectionObserver' in window)) {
        lazySections.forEach(loadSection);
        return;
    }

    const observer = new IntersectionObserver(function (entries) {
        entries.forEach(function (entry) {
            if (!entry.isIntersecting) {
                return;
            }

            observer.unobserve(entry.target);
            loadSection(entry.target);
        });
    }, {
        root: null,
        rootMargin: '700px 0px',
        threshold: 0.01
    });

    lazySections.forEach(function (section) {
        observer.observe(section);
    });
});
