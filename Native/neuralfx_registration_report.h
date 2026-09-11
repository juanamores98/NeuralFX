// Informe estructurado del registro de recursos de movimiento. MIT-0.
//
// <b>Por qué existe.</b> `NeuralFX_RegisterMotion` devolvía 0 por once condiciones distintas
// sin decir cuál. En CS1 el registro fallaba desde el primer fotograma y desde el lado
// administrado no se podía acotar más: el descriptor real de la textura solo lo puede leer
// C++. Tres sesiones de juego terminaron sin identificar la causa. Este informe existe para
// que la primera partida normal la deje escrita, sin pedirle al usuario ningún paso.
//
// <b>Qué es y qué no es.</b> Es una observación del recurso que llegó y de la etapa en que se
// rechazó. No es un diagnóstico de por qué Unity entrega ese recurso, ni una causa confirmada
// de nada aguas arriba. El consumidor debe presentarlo como lo que es.
#pragma once
#include <cstdint>
#include <cstddef>

// Etapa alcanzada antes de detenerse. Ordenadas: una etapa mayor implica que las anteriores
// se superaron.
static constexpr uint32_t
    NFX_REG_STAGE_INPUT = 0,       // validación de los campos que llegan por CPU
    NFX_REG_STAGE_INTERFACE = 1,   // negociación COM hasta ID3D11Texture2D
    NFX_REG_STAGE_DESCRIPTOR = 2,  // lectura y validación del descriptor real
    NFX_REG_STAGE_VIEW = 3,        // creación de la vista de lectura
    NFX_REG_STAGE_SLOT = 4,        // reserva de hueco y retención
    NFX_REG_STAGE_DONE = 5;

// Motivo único. Nunca se agrupan dos causas en el mismo valor: ese fue exactamente el
// problema que este informe viene a resolver.
static constexpr uint32_t
    NFX_REG_OK = 0,
    NFX_REG_NULL_POINTER = 1,      // puntero, cámara o época nulos
    NFX_REG_BAD_IDENTITY = 2,      // cámara o época fuera de contrato
    NFX_REG_QUERY_INTERFACE = 3,   // el puntero no expone ID3D11Texture2D ni una vista con recurso
    NFX_REG_FORMAT = 4,            // formato distinto del contrato de movimiento
    NFX_REG_SAMPLES = 5,           // multisample
    NFX_REG_MIPS = 6,              // cadena de mips
    NFX_REG_ARRAY = 7,             // array o cubemap
    NFX_REG_BIND_SRV = 8,          // sin permiso de lectura como shader resource
    NFX_REG_VIEW = 9,              // CreateShaderResourceView falló; mirar hresult
    NFX_REG_SLOTS = 10,            // tabla de huecos agotada
    NFX_REG_DIMENSION = 11,        // extensión nula o fuera de límite
    NFX_REG_REASON_COUNT = 12;

// Por que el selector no uso los vectores registrados. Un solo valor por causa: mezclarlas es
// el defecto que ya costo tres sesiones en el registro y otra en la reserva.
static constexpr uint32_t
    NFX_SEL_USED = 0,            // se usaron los vectores de Unity
    NFX_SEL_NO_HANDLE = 1,       // el frame no traia handle
    NFX_SEL_UNKNOWN_HANDLE = 2,  // el handle no esta en la tabla
    NFX_SEL_NOT_RESERVED = 3,    // el hueco no estaba reservado para este frame
    NFX_SEL_CAMERA = 4,          // el hueco pertenece a otra camara
    NFX_SEL_EPOCH = 5,           // el hueco pertenece a otra epoca
    NFX_SEL_DEVICE = 6,          // el recurso vive en otro dispositivo D3D11
    NFX_SEL_VIEW = 7,            // la vista no apunta a su propia textura
    NFX_SEL_EXTENT = 8;          // el recurso no mide lo que el frame declara

#pragma pack(push, 4)
// Ancho fijo en los dos lados. El consumidor administrado valida `size` y `version` antes de
// leer nada más; un puente antiguo simplemente no exporta la consulta.
struct NeuralFxRegistrationReport {
    uint32_t size, version;
    uint32_t request_serial;      // cuántos registros se han intentado en este proceso
    uint32_t stage, reason;
    int32_t  hresult;             // 0 si ninguna llamada COM falló
    uint32_t camera, epoch;
    uint32_t width, height;
    uint32_t format;              // DXGI_FORMAT tal cual lo declara el recurso
    uint32_t mip_levels, array_size, sample_count, sample_quality;
    uint32_t bind_flags, misc_flags, usage, cpu_access;
    uint32_t from_view;           // 1 si hubo que pedir el recurso a una vista
    uint32_t slots_used, slots_total;
    uint32_t accepted, rejected;
    // Version 2. Registrar el recurso es solo la mitad: cada frame hay que reservar un turno y
    // alguien tiene que devolverlo cuando la GPU termina. Si las devoluciones no ocurren, los
    // turnos se agotan y la ruta muere igual de callada que antes.
    uint32_t reserved_now;        // turnos en vuelo ahora mismo
    uint32_t reserve_ok;          // reservas concedidas desde el arranque
    uint32_t reserve_denied;      // reservas negadas: el turno seguia ocupado
    uint32_t completed;           // devoluciones por trabajo GPU terminado o cancelacion
    uint32_t reserve_expired;     // turnos reclamados por caducidad: nadie los reclamo nunca
    // Version 3. Registrar el recurso y conseguir turno tampoco basta: el selector del addon
    // vuelve a comprobarlo todo contra el frame, y si algo no cuadra cae al descriptor optico
    // sin decir que. Aqui queda dicho.
    uint32_t select_reason;          // NFX_SEL_* del ultimo frame que traia handle
    uint32_t select_native;          // frames servidos con vectores de Unity
    uint32_t select_fallback;        // frames con handle que aun asi cayeron al optico
    uint32_t select_frame_width, select_frame_height;   // lo que el frame declaraba
    uint32_t select_slot_width, select_slot_height;     // lo que el recurso media
    uint32_t select_identity;        // bit 1 camara distinta, bit 2 epoca distinta
    // Version 4. Un puntero de dispositivo distinto NO prueba que sea otro dispositivo: ReShade
    // envuelve ID3D11Device, y el recurso declara el real. Se registra como se establecio la
    // identidad, y los LUID de ambos adaptadores para poder distinguir "mismo aparato detras de
    // una envoltura" de "dos dispositivos de verdad".
    uint32_t select_device_match;    // 0 ninguna, 1 mismo puntero, 2 identidad DXGI, 3 el consumidor pudo crear su vista
    int32_t  select_probe_hresult;   // resultado de esa prueba; 0 si no hizo falta
    uint32_t select_owner_luid_low, select_device_luid_low;
    int32_t  select_owner_luid_high, select_device_luid_high;
    uint32_t counts[NFX_REG_REASON_COUNT];  // cuántas veces cada motivo, desde el arranque
};
#pragma pack(pop)
static_assert(sizeof(NeuralFxRegistrationReport) == 220);
static_assert(offsetof(NeuralFxRegistrationReport, reason) == 16);
static_assert(offsetof(NeuralFxRegistrationReport, reserved_now) == 96);
static_assert(offsetof(NeuralFxRegistrationReport, select_reason) == 116);
static_assert(offsetof(NeuralFxRegistrationReport, select_device_match) == 148);
static_assert(offsetof(NeuralFxRegistrationReport, counts) == 172);
