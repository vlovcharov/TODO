import axios from 'axios';

const api = axios.create({ baseURL: '/api' });

export const tasksApi = {
  getDay:    (date)                      => api.get('/tasks/day', { params: { date } }).then(r => r.data),
  getRange:  (from, to)                  => api.get('/tasks/range', { params: { from, to } }).then(r => r.data),
  create:    (data)                      => api.post('/tasks', data).then(r => r.data),
  update:    (id, data)                  => api.put(`/tasks/${id}`, data).then(r => r.data),
  toggle:    (id, date)                  => api.post(`/tasks/${id}/toggle`, { date }).then(r => r.data),
  delete:    (id)                        => api.delete(`/tasks/${id}`),
  restore:   (id)                        => api.post(`/tasks/${id}/restore`).then(r => r.data),
  move:      (id, newDate, newSortOrder) => api.post(`/tasks/${id}/move`, { newDate, newSortOrder }).then(r => r.data),
  reorder:   (taskIds)                   => api.post('/tasks/reorder', { taskIds }),
  moveToTop: (id, date)                  => api.post(`/tasks/${id}/top`, { date }).then(r => r.data),
};

export const configApi = {
  get: () => api.get('/config').then(r => r.data),
  update: (data) => api.put('/config', data).then(r => r.data),
};

export const backupApi = {
  exportJson: () => api.get('/tasks/export', { responseType: 'blob' }),
  import: (file) => {
    const form = new FormData();
    form.append('file', file);
    return api.post('/tasks/import', form, {
      headers: { 'Content-Type': 'multipart/form-data' }
    }).then(r => r.data);
  },
};

export const epicsApi = {
  getAll:  (date)      => api.get('/epics', { params: date ? { date } : {} }).then(r => r.data),
  create:  (data)      => api.post('/epics', data).then(r => r.data),
  update:  (id, data)  => api.put(`/epics/${id}`, data).then(r => r.data),
  delete:  (id)        => api.delete(`/epics/${id}`),
};

export const scheduleApi = {
  getForDate: (date)      => api.get('/schedule', { params: { date } }).then(r => r.data),
  create:     (data)      => api.post('/schedule', data).then(r => r.data),
  update:     (id, data)  => api.put(`/schedule/${id}`, data).then(r => r.data),
  delete:     (id)        => api.delete(`/schedule/${id}`),
};
