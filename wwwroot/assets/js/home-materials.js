            document.addEventListener('DOMContentLoaded', function () {
                const materialsSwiperElement = document.querySelector('.materials-grid-swiper');
                let materialsSwiperInstance = null;

                let isDraggingSlides = false;
                let suppressClickUntil = 0;
                let pointerDownX = 0;
                let pointerDownY = 0;
                let isPointerDownInSwiper = false;
                let hoverRotationTimers = new WeakMap();

                if (materialsSwiperElement && typeof Swiper !== 'undefined') {
                    const totalSlides = materialsSwiperElement.querySelectorAll('.swiper-slide:not(.swiper-slide-duplicate)').length;

                    materialsSwiperInstance = new Swiper(materialsSwiperElement, {
                        speed: 520,

                        loop: totalSlides > 4,
                        rewind: false,
                        loopAdditionalSlides: 4,
                        loopPreventsSliding: false,

                        slidesPerView: 1,
                        slidesPerGroup: 1,
                        spaceBetween: 16,

                        grabCursor: true,
                        allowTouchMove: true,
                        simulateTouch: true,
                        followFinger: true,

                        freeMode: {
                            enabled: true,
                            momentum: true,
                            momentumRatio: 0.5,
                            momentumVelocityRatio: 0.72,
                            minimumVelocity: 0.02,
                            momentumBounce: false,
                            sticky: true
                        },
                        centeredSlides: false,
                        roundLengths: true,

                        touchEventsTarget: 'container',
                        touchRatio: 1.16,
                        touchAngle: 28,
                        threshold: 3,

                        longSwipes: true,
                        longSwipesRatio: 0.12,
                        longSwipesMs: 180,
                        shortSwipes: true,

                        resistance: true,
                        resistanceRatio: 0.9,

                        preventClicks: true,
                        preventClicksPropagation: true,

                        touchStartPreventDefault: false,
                        touchMoveStopPropagation: true,
                        touchReleaseOnEdges: false,

                        observer: true,
                        observeParents: true,
                        updateOnWindowResize: true,
                        watchOverflow: true,

                        autoplay: false,

                        pagination: {
                            el: '.materials-products-pagination',
                            clickable: true
                        },

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
                            init: function () {
                                this.update();
                            },

                            touchStart: function () {
                                isDraggingSlides = false;
                                materialsSwiperElement.classList.add('swiper-grabbing');
                            },

                            sliderFirstMove: function () {
                                isDraggingSlides = true;
                                suppressClickUntil = Date.now() + 500;
                                materialsSwiperElement.classList.add('swiper-grabbing');
                            },

                            touchEnd: function () {
                                materialsSwiperElement.classList.remove('swiper-grabbing');
                                document.body.classList.remove('swiper-grabbing');

                                suppressClickUntil = Date.now() + (isDraggingSlides ? 500 : 0);

                                setTimeout(function () {
                                    isDraggingSlides = false;
                                }, 60);
                            },

                            transitionEnd: function () {
                                materialsSwiperElement.classList.remove('swiper-grabbing');
                                document.body.classList.remove('swiper-grabbing');
                            },

                            resize: function () {
                                this.update();
                            }
                        }
                    });

                    const forceReleaseDraggingState = function () {
                        materialsSwiperElement.classList.remove('swiper-grabbing');
                        document.body.classList.remove('swiper-grabbing');

                        if (materialsSwiperInstance) {
                            materialsSwiperInstance.allowTouchMove = true;
                            materialsSwiperInstance.allowClick = true;
                            materialsSwiperInstance.update();
                        }

                        setTimeout(function () {
                            isDraggingSlides = false;
                        }, 60);
                    };

                    [
                        'mouseup',
                        'pointerup',
                        'pointercancel',
                        'touchend',
                        'touchcancel',
                        'dragend',
                        'mouseleave',
                        'blur'
                    ].forEach(function (eventName) {
                        window.addEventListener(eventName, forceReleaseDraggingState, { passive: true });
                    });

                    [
                        'mouseup',
                        'pointerup',
                        'pointercancel',
                        'touchend',
                        'touchcancel',
                        'mouseleave',
                        'lostpointercapture'
                    ].forEach(function (eventName) {
                        materialsSwiperElement.addEventListener(eventName, forceReleaseDraggingState, { passive: true });
                    });

                    materialsSwiperElement.addEventListener('pointerdown', function (event) {
                        pointerDownX = event.clientX;
                        pointerDownY = event.clientY;
                        isDraggingSlides = false;
                        isPointerDownInSwiper = true;

                        hoverRotationTimers = hoverRotationTimers || new WeakMap();
                        materialsSwiperElement.querySelectorAll('.materials-hover-image').forEach(function (img) {
                            const timer = hoverRotationTimers.get(img);
                            if (timer) {
                                clearInterval(timer);
                                hoverRotationTimers.delete(img);
                            }
                            if (img.dataset.defaultImage) {
                                img.src = img.dataset.defaultImage;
                            }
                        });
                    }, { passive: true });

                    ['pointerup', 'pointercancel', 'touchend', 'touchcancel', 'lostpointercapture', 'mouseup'].forEach(function (eventName) {
                        materialsSwiperElement.addEventListener(eventName, function () {
                            isPointerDownInSwiper = false;
                        }, { passive: true });
                    });

                    materialsSwiperElement.addEventListener('pointermove', function (event) {
                        const dx = Math.abs(event.clientX - pointerDownX);
                        const dy = Math.abs(event.clientY - pointerDownY);

                        if (dx > 4 && dx > dy) {
                            isDraggingSlides = true;
                            suppressClickUntil = Date.now() + 500;
                        }
                    }, { passive: true });

                    materialsSwiperElement.querySelectorAll('img, a').forEach(function (node) {
                        node.setAttribute('draggable', 'false');

                        node.addEventListener('dragstart', function (event) {
                            event.preventDefault();
                        });
                    });

                    materialsSwiperElement.querySelectorAll('a.materials-card-v2').forEach(function (linkElement) {
                        linkElement.addEventListener('click', function (event) {
                            if (isDraggingSlides || Date.now() < suppressClickUntil) {
                                event.preventDefault();
                                event.stopPropagation();
                            }
                        }, true);
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
                }

                const materialsCategorySelect = document.getElementById('materialsCategorySelect');

                if (materialsCategorySelect && materialsSwiperElement) {
                    const slides = Array.from(materialsSwiperElement.querySelectorAll('.swiper-slide'));

                    const applyCategoryFilter = function () {
                        const selectedCategory = materialsCategorySelect.value;

                        slides.forEach(function (slideElement) {
                            const slideCategory = slideElement.getAttribute('data-category') || '';
                            const shouldShow = !selectedCategory || slideCategory === selectedCategory;

                            slideElement.style.display = shouldShow ? '' : 'none';
                        });

                        if (materialsSwiperInstance) {
                            materialsSwiperInstance.update();

                            if (materialsSwiperInstance.params.loop && typeof materialsSwiperInstance.slideToLoop === 'function') {
                                materialsSwiperInstance.slideToLoop(0, 0);
                            } else {
                                materialsSwiperInstance.slideTo(0, 0);
                            }
                        }
                    };

                    materialsCategorySelect.addEventListener('change', applyCategoryFilter);
                    applyCategoryFilter();
                }

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

                if (materialsSwiperInstance) {
                    materialsSwiperInstance.on('slideChangeTransitionEnd', bindMaterialsHoverRotation);
                    materialsSwiperInstance.on('update', bindMaterialsHoverRotation);
                }
            });
