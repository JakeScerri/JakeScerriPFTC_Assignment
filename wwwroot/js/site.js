// wwwroot/js/site.js
$(document).ready(function() {
    let isSubmitting = false;

    $('#ticketForm').submit(function(e) {
        e.preventDefault();

        // Strong prevention of duplicate submissions
        if (isSubmitting) {
            console.log('Preventing duplicate submission');
            return false;
        }

        isSubmitting = true;
        console.log('Form submission started');
        $('#submitButton').prop('disabled', true);

        var formData = new FormData();
        formData.append('title', $('#title').val());
        formData.append('description', $('#description').val());
        formData.append('priority', $('#priority').val());

        // Add files
        var fileInput = $('#screenshots')[0];
        for (var i = 0; i < fileInput.files.length; i++) {
            formData.append('screenshots', fileInput.files[i]);
        }

        // Show progress bar
        $('.progress').removeClass('d-none');

        $.ajax({
            url: '/api/tickets',
            type: 'POST',
            data: formData,
            processData: false,
            contentType: false,
            xhr: function() {
                var xhr = new XMLHttpRequest();
                xhr.upload.addEventListener('progress', function(e) {
                    if (e.lengthComputable) {
                        var percent = Math.round((e.loaded / e.total) * 100);
                        $('#uploadProgress').css('width', percent + '%');
                        $('#uploadProgress').text(percent + '%');
                    }
                }, false);
                return xhr;
            },
            success: function(response) {
                console.log('Form submission succeeded');
                $('#submitResult').removeClass('d-none alert-danger').addClass('alert-success');
                $('#submitResult').html('Ticket submitted successfully! You can track its status later.');

                $('#ticketForm')[0].reset();
                $('.progress').addClass('d-none');
                $('#uploadProgress').css('width', '0%');

                // Re-enable form submission after a delay
                setTimeout(function() {
                    isSubmitting = false;
                    $('#submitButton').prop('disabled', false);
                }, 3000);
            },
            error: function(xhr, status, error) {
                console.log('Form submission failed:', error);
                // If unauthorized (401), redirect to login
                if (xhr.status === 401) {
                    window.location.href = '/api/auth/login';
                    return;
                }

                $('#submitResult').removeClass('d-none alert-success').addClass('alert-danger');
                $('#submitResult').text('Error submitting ticket: ' + (xhr.responseText || error));

                $('.progress').addClass('d-none');

                // Re-enable form immediately on error
                isSubmitting = false;
                $('#submitButton').prop('disabled', false);
            },
            complete: function() {
                console.log('Form submission completed');
                // Additional safeguard to ensure form is re-enabled
                setTimeout(function() {
                    isSubmitting = false;
                    $('#submitButton').prop('disabled', false);
                }, 5000);
            }
        });
    });
});