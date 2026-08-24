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
