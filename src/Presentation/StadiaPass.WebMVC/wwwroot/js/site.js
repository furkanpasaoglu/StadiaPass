// Input masks for the checkout panel. Driven entirely by data-mask attributes, so this file stays inert on
// every page that has no card fields on it.
//
// The masks are here to make typing pleasant and nothing more: the server parses these fields itself, so a
// browser with scripting turned off still submits something the API understands.
(function () {
    'use strict';

    var digitsOnly = function (value) {
        return value.replace(/\D/g, '');
    };

    // Reformatting an input parks the caret at the end unless it is put back, which makes correcting a typo
    // in the middle of a card number infuriating. Positions are therefore counted in digits rather than in
    // characters: whatever the separators do, the caret comes back to the same digit the typist was on.
    var caretToDigitOffset = function (value, caret) {
        return digitsOnly(value.slice(0, caret)).length;
    };

    var digitOffsetToCaret = function (value, digitOffset) {
        if (digitOffset <= 0) {
            return 0;
        }

        var seen = 0;

        for (var index = 0; index < value.length; index++) {
            if (/\d/.test(value[index])) {
                seen++;

                if (seen === digitOffset) {
                    return index + 1;
                }
            }
        }

        return value.length;
    };

    // Four digit groups, up to the nineteen a card number can carry.
    var asCardNumber = function (value) {
        return digitsOnly(value).slice(0, 19).replace(/(\d{4})(?=\d)/g, '$1 ');
    };

    var asExpiry = function (value) {
        var digits = digitsOnly(value).slice(0, 4);

        // A lone digit above one can only ever have been a month with a leading zero. Nobody means month 90.
        if (digits.length === 1 && digits > '1') {
            digits = '0' + digits;
        }

        if (digits.length >= 2) {
            var month = Math.min(Math.max(parseInt(digits.slice(0, 2), 10) || 1, 1), 12);
            digits = String(month).padStart(2, '0') + digits.slice(2);
        }

        return digits.length > 2 ? digits.slice(0, 2) + ' / ' + digits.slice(2) : digits;
    };

    var asSecurityCode = function (value) {
        return digitsOnly(value).slice(0, 4);
    };

    var formats = {
        'card-number': asCardNumber,
        'expiry': asExpiry,
        'security-code': asSecurityCode
    };

    var apply = function (input, format) {
        var reformat = function () {
            var caret = input.selectionStart === null ? input.value.length : input.selectionStart;
            var before = digitsOnly(input.value);
            var formatted = format(input.value);
            var after = digitsOnly(formatted);

            // A mask can add or drop digits of its own - the expiry pads a lone month with a leading zero,
            // the card number stops at nineteen. Counting digits only identifies the same spot if the caret
            // is moved by however many the mask put in front of it.
            var digitOffset = caretToDigitOffset(input.value, caret) + after.length - before.length;

            if (formatted !== input.value) {
                input.value = formatted;
            }

            var restored = digitOffsetToCaret(formatted, digitOffset);
            input.setSelectionRange(restored, restored);
        };

        // Backspace against a separator would otherwise delete the separator, which the mask immediately
        // puts back - so the key appears to do nothing. Stepping over it first deletes the digit the typist
        // was actually aiming at.
        input.addEventListener('beforeinput', function (event) {
            if (event.inputType !== 'deleteContentBackward' || input.selectionStart !== input.selectionEnd) {
                return;
            }

            var caret = input.selectionStart;

            while (caret > 0 && !/\d/.test(input.value[caret - 1])) {
                caret--;
            }

            if (caret !== input.selectionStart) {
                input.setSelectionRange(caret, caret);
            }
        });

        input.addEventListener('input', reformat);
        input.addEventListener('blur', reformat);
    };

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('[data-mask]').forEach(function (input) {
            var format = formats[input.dataset.mask];

            if (format) {
                apply(input, format);
            }
        });
    });
})();

// Checkout validation.
//
// The masks above only stop nonsense characters: they will happily accept nineteen digits that no bank ever
// issued, and the customer finds out after a round trip. These checks mirror the rules the API enforces -
// same digit counts, same Luhn, same "a card is good through the last day of its expiry month" - so a typo
// is caught next to the field that has it. The server still decides: with scripting off nothing here runs
// and the API rejects the charge exactly as before.
(function () {
    'use strict';

    var digitsOnly = function (value) {
        return String(value || '').replace(/\D/g, '');
    };

    // The check digit every card number carries. Catches a mistyped digit or two transposed ones.
    var passesLuhn = function (digits) {
        var sum = 0;
        var doubling = false;

        for (var index = digits.length - 1; index >= 0; index--) {
            var value = digits.charCodeAt(index) - 48;

            if (doubling) {
                value *= 2;

                if (value > 9) {
                    value -= 9;
                }
            }

            sum += value;
            doubling = !doubling;
        }

        return digits.length > 0 && sum % 10 === 0;
    };

    var checks = {
        'card-number': function (value) {
            var digits = digitsOnly(value);

            if (digits.length === 0) { return 'A card number is required.'; }
            if (digits.length < 13 || digits.length > 19) { return 'A card number has between 13 and 19 digits.'; }
            if (!passesLuhn(digits)) { return 'That card number is not valid.'; }

            return null;
        },
        'expiry': function (value) {
            var digits = digitsOnly(value);

            if (digits.length === 0) { return 'The card expiry is required.'; }
            if (digits.length !== 4) { return 'The expiry looks like MM / YY, for example 12 / 30.'; }

            var month = parseInt(digits.slice(0, 2), 10);
            var year = 2000 + parseInt(digits.slice(2), 10);

            if (month < 1 || month > 12) { return 'The expiry month must be between 1 and 12.'; }

            var now = new Date();

            // Good through the last day of the month it names.
            if (year < now.getFullYear() || (year === now.getFullYear() && month < now.getMonth() + 1)) {
                return 'The card has expired.';
            }

            if (year > now.getFullYear() + 20) { return 'That expiry year is not plausible.'; }

            return null;
        },
        'security-code': function (value) {
            var digits = digitsOnly(value);

            if (digits.length === 0) { return 'The security code is required.'; }
            if (digits.length < 3 || digits.length > 4) { return 'The security code is three or four digits.'; }

            return null;
        }
    };

    var messageFor = function (input) {
        var next = input.nextElementSibling;

        if (next && next.classList.contains('js-field-error')) {
            return next;
        }

        var element = document.createElement('span');
        element.className = 'text-danger js-field-error d-block mt-1';
        input.insertAdjacentElement('afterend', element);

        return element;
    };

    var check = function (input) {
        var problem = checks[input.dataset.mask](input.value);
        var message = messageFor(input);

        input.classList.toggle('input-validation-error', problem !== null);
        message.textContent = problem || '';

        return problem === null;
    };

    var clear = function (input) {
        input.classList.remove('input-validation-error');
        messageFor(input).textContent = '';
    };

    document.addEventListener('DOMContentLoaded', function () {
        var inputs = Array.prototype.slice.call(document.querySelectorAll('[data-mask]'))
            .filter(function (input) { return checks[input.dataset.mask]; });

        if (inputs.length === 0) { return; }

        inputs.forEach(function (input) {
            // Nagging while somebody is still typing their card number is worse than useless, so a field is
            // only judged once they leave it - and forgiven the moment they come back to fix it.
            input.addEventListener('blur', function () { check(input); });
            input.addEventListener('input', function () { clear(input); });
        });

        inputs[0].form.addEventListener('submit', function (event) {
            var firstBad = null;

            inputs.forEach(function (input) {
                if (!check(input) && firstBad === null) {
                    firstBad = input;
                }
            });

            if (firstBad !== null) {
                event.preventDefault();
                firstBad.focus();
            }
        });
    });
})();

// Destructive actions ask first.
//
// Delete is one click next to Edit in every back office table, it takes a venue or a role with it, and there
// is no undo behind it. A form carrying data-confirm has to be acknowledged before it posts.
(function () {
    'use strict';

    document.addEventListener('submit', function (event) {
        var message = event.target.dataset ? event.target.dataset.confirm : null;

        if (message && !window.confirm(message)) {
            event.preventDefault();
        }
    });
})();
