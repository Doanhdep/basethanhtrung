document.addEventListener('DOMContentLoaded', function () {
    let isDraggingSlides = false;
    let suppressClickUntil = 0;
    let pointerDownX = 0;
    let pointerDownY = 0;
    let isPointerDownInSwiper = false;
    let hoverRotationTimers = new WeakMap();

    const swiperSelectors = [
        { swiper: '.materials-row1-swiper', pagination: '.materials-row1-pagination' },
        { swiper: '.materials-row2-swiper', pagination: '.materials-row2-pagination' },
        { swiper: '.materials-grid-swiper', pagination: '.materials-products-pagination' }
    ];

    const swiperInstances = [];

    swiperSelectors.forEach(function (entry) {
        const swiperElement = document.querySelector(entry.swiper);
        if (!swiperElement || typeof Swiper === 'undefined') {
            return;
        }

        const totalSlides = swiperElement.querySelectorAll('.swiper-slide:not(.swiper-slide-duplicate)').length;

        const instance = new Swiper(swiperElement, {
            speed: 420,
            loop: totalSlides > 4,
            rewind: false,
            loopAdditionalSlides: 2,
            loopPreventsSliding: false,

            slidesPerView: 1,
            slidesPerGroup: 1,
            spaceBetween: 16,

            grabCursor: true,
            allowTouchMove: true,
            simulateTouch: true,
            followFinger: true,

            touchRatio: 1,
            touchAngle: 45,
            threshold: 5,

            longSwipes: true,
            longSwipesRatio: 0.15,
            longSwipesMs: 220,
            shortSwipes: true,

            resistance: true,
            resistanceRatio: 0.85,

            preventClicks: true,
            preventClicksPropagation: true,
            touchStartPreventDefault: false,

            observer: true,
            observeParents: true,
            watchOverflow: true,

            autoplay: false,

            pagination: entry.pagination && document.querySelector(entry.pagination) ? {
                el: entry.pagination,
                clickable: true
            } : false,

            breakpoints: {
                576: {
                    slidesPerView: 2,
                    slidesPerGroup: 1,
                    spaceBetween: 18
                },
                992: {
                    slidesPerView: 3,
                    slidesPerGroup: 1,
                    spaceBetween: 20
                },
                1200: {
                    slidesPerView: 4,
                    slidesPerGroup: 1,
                    spaceBetween: 20
                }
            },

            on: {
                touchStart: function () {
                    isDraggingSlides = false;
                    swiperElement.classList.add('swiper-grabbing');
                },

                sliderMove: function () {
                    isDraggingSlides = true;
                    suppressClickUntil = Date.now() + 350;
                },

                touchEnd: function () {
                    swiperElement.classList.remove('swiper-grabbing');
                    document.body.classList.remove('swiper-grabbing');

                    setTimeout(function () {
                        isDraggingSlides = false;
                    }, 60);
                },

                transitionEnd: function () {
                    swiperElement.classList.remove('swiper-grabbing');
                    document.body.classList.remove('swiper-grabbing');
                }
            }
        });

        swiperInstances.push({ element: swiperElement, instance: instance });

        swiperElement.addEventListener('pointerdown', function (event) {
            pointerDownX = event.clientX;
            pointerDownY = event.clientY;
            isDraggingSlides = false;
            isPointerDownInSwiper = true;
        }, { passive: true });

        ['pointerup', 'pointercancel', 'touchend', 'touchcancel'].forEach(function (eventName) {
            swiperElement.addEventListener(eventName, function () {
                isPointerDownInSwiper = false;
            }, { passive: true });
        });

        swiperElement.querySelectorAll('img, a').forEach(function (node) {
            node.setAttribute('draggable', 'false');

            node.addEventListener('dragstart', function (event) {
                event.preventDefault();
            });
        });

        swiperElement.querySelectorAll('a.materials-card-v2').forEach(function (linkElement) {
            linkElement.addEventListener('click', function (event) {
                if (isDraggingSlides || Date.now() < suppressClickUntil) {
                    event.preventDefault();
                    event.stopPropagation();
                }
            }, true);
        });
    });

    const forceReleaseDraggingState = function () {
        document.querySelectorAll('.materials-grid-swiper, .materials-row1-swiper, .materials-row2-swiper').forEach(function (el) {
            el.classList.remove('swiper-grabbing');
        });
        document.body.classList.remove('swiper-grabbing');

        setTimeout(function () {
            isDraggingSlides = false;
        }, 60);
    };

    ['mouseup', 'pointerup', 'touchend', 'touchcancel'].forEach(function (eventName) {
        window.addEventListener(eventName, forceReleaseDraggingState, { passive: true });
    });

    document.addEventListener('click', function (event) {
        const target = event.target;

        if (!target || !(target instanceof Element)) {
            return;
        }

        const inMaterialsCard = target.closest('#materials a.materials-card-v2');

        if (!inMaterialsCard) {
            return;
        }

        if (isDraggingSlides || Date.now() < suppressClickUntil) {
            event.preventDefault();
            event.stopPropagation();
        }
    }, true);

    function bindMaterialsHoverRotation() {
        document.querySelectorAll('#materials .materials-card-media').forEach(function (mediaElement) {
            const imageElement = mediaElement.querySelector('.materials-hover-image');
            if (!imageElement || imageElement.dataset.rotationInitialized === 'true') {
                return;
            }

            const sequence = (imageElement.dataset.imageSequence || '')
                .split('|')
                .map(function (item) { return item.trim(); })
                .filter(Boolean);

            if (sequence.length <= 1) {
                imageElement.dataset.rotationInitialized = 'true';
                return;
            }

            const defaultImage = imageElement.dataset.defaultImage || sequence[0];
            let currentIndex = 0;

            const stopHoverRotation = function () {
                const oldTimer = hoverRotationTimers.get(imageElement);
                if (oldTimer) {
                    clearInterval(oldTimer);
                    hoverRotationTimers.delete(imageElement);
                }

                currentIndex = 0;
                imageElement.src = defaultImage;
            };

            const startHoverRotation = function () {
                if (isPointerDownInSwiper || hoverRotationTimers.get(imageElement)) {
                    return;
                }

                currentIndex = 1;
                imageElement.src = sequence[currentIndex];

                const timer = setInterval(function () {
                    currentIndex = (currentIndex + 1) % sequence.length;
                    imageElement.src = sequence[currentIndex];
                }, 900);

                hoverRotationTimers.set(imageElement, timer);
            };

            imageElement.dataset.rotationInitialized = 'true';
            mediaElement.addEventListener('mouseenter', startHoverRotation);
            mediaElement.addEventListener('mouseleave', stopHoverRotation);
            mediaElement.addEventListener('mousemove', startHoverRotation);
            mediaElement.addEventListener('touchstart', stopHoverRotation, { passive: true });
            mediaElement.addEventListener('pointerdown', stopHoverRotation, { passive: true });
        });
    }

    bindMaterialsHoverRotation();

    swiperInstances.forEach(function (entry) {
        if (entry.instance) {
            entry.instance.on('slideChangeTransitionEnd', bindMaterialsHoverRotation);
            entry.instance.on('update', bindMaterialsHoverRotation);
        }
    });
});
